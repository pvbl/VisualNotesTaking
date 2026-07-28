using System.Collections.Concurrent;

using Microsoft.EntityFrameworkCore;

using Shouldly;

using VisualNotes.Core.Models;
using VisualNotes.Core.Services;
using VisualNotes.Infrastructure.Persistence;
using VisualNotes.Infrastructure.Processing;

namespace VisualNotes.IntegrationTests;

public sealed class DurableAnalysisJobProcessorTests : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "visualnotes-jobs-" + Guid.NewGuid().ToString("N"));
    private Guid _screenshotId;
    private Guid[] _providerIds = [];

    [Fact]
    public async Task Restart_during_work_recovers_without_losing_or_duplicating_result()
    {
        await using var factory = await CreateFactoryAsync();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blocking = new Handler(async (_, token) => { started.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return Result(); });
        var job = NewJob("restart");
        await using (var first = new DurableAnalysisJobProcessor(factory, blocking, new(1, 1)))
        {
            await first.EnqueueAsync(job);
            var drain = first.RunAutomaticAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }

        var successful = new Handler((_, _) => Task.FromResult(Result()));
        await using (var second = new DurableAnalysisJobProcessor(factory, successful, new(1, 1)))
            (await second.RunAutomaticAsync()).ShouldBe(1);

        await using var db = await factory.CreateDbContextAsync();
        var persisted = await db.AnalysisJobs.Include(x => x.Result).Include(x => x.AttemptHistory).SingleAsync();
        persisted.JobStatus.ShouldBe(AnalysisJobStatus.Completed);
        persisted.Result.ShouldNotBeNull();
        persisted.AttemptHistory.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Hundreds_of_jobs_obey_limits_and_execute_exactly_once()
    {
        await using var factory = await CreateFactoryAsync();
        var handler = new Handler(async (job, token) => { await Task.Delay(2, token); return Result(job.Id.ToString()); });
        await using var processor = new DurableAnalysisJobProcessor(factory, handler, new(12, 3));
        for (var i = 0; i < 300; i++) await processor.EnqueueAsync(NewJob($"stress-{i}", i % 4));

        (await processor.RunAutomaticAsync()).ShouldBe(300);

        handler.MaximumGlobal.ShouldBeLessThanOrEqualTo(12);
        handler.MaximumByProvider.Values.ShouldAllBe(x => x <= 3);
        handler.Executions.Values.ShouldAllBe(x => x == 1);
        await using var db = await factory.CreateDbContextAsync();
        (await db.AnalysisJobs.CountAsync(x => x.JobStatus == AnalysisJobStatus.Completed)).ShouldBe(300);
        (await db.CaptureAnalyses.CountAsync()).ShouldBe(300);
    }

    [Theory]
    [InlineData(LanguageModelErrorKind.Authentication, "authentication")]
    [InlineData(LanguageModelErrorKind.RateLimited, "limit")]
    [InlineData(LanguageModelErrorKind.BudgetExhausted, "limit")]
    [InlineData(LanguageModelErrorKind.Timeout, "network")]
    [InlineData(LanguageModelErrorKind.ServiceUnavailable, "network")]
    [InlineData(LanguageModelErrorKind.InvalidResponse, "invalid-response")]
    [InlineData(LanguageModelErrorKind.InvalidRequest, "configuration")]
    [InlineData(LanguageModelErrorKind.Cancelled, "unknown")]
    public async Task Language_model_failures_are_classified_and_persisted(
        LanguageModelErrorKind kind, string classification)
    {
        await using var factory = await CreateFactoryAsync();
        var handler = new Handler((_, _) => throw new LanguageModelException(kind, "controlled failure"));
        await using var processor = new DurableAnalysisJobProcessor(factory, handler, new(1, 1, RetryBaseDelay: TimeSpan.Zero));
        await processor.EnqueueAsync(NewJob($"failure-{kind}", maximumAttempts: 1));

        (await processor.RunAutomaticAsync()).ShouldBe(1);

        await using var db = await factory.CreateDbContextAsync();
        var persisted = await db.AnalysisJobs.Include(x => x.AttemptHistory).SingleAsync();
        persisted.JobStatus.ShouldBe(AnalysisJobStatus.Failed);
        persisted.Error.ShouldStartWith(classification + ":");
        persisted.CompletedAt.ShouldNotBeNull();
        persisted.NextAttemptAt.ShouldBeNull();
        persisted.AttemptHistory.Single().Error.ShouldNotBeNull().ShouldContain("controlled failure");
    }

    [Theory]
    [InlineData("http", "network")]
    [InlineData("schema", "invalid-response")]
    [InlineData("unknown", "unknown")]
    public async Task Non_provider_failures_are_classified_and_persisted(string errorKind, string classification)
    {
        await using var factory = await CreateFactoryAsync();
        Exception error = errorKind switch
        {
            "http" => new HttpRequestException("offline"),
            "schema" => new AnalysisResponseValidationException("invalid payload"),
            _ => new InvalidOperationException("unexpected")
        };
        var handler = new Handler((_, _) => throw error);
        await using var processor = new DurableAnalysisJobProcessor(factory, handler, new(1, 1));
        await processor.EnqueueAsync(NewJob($"failure-{errorKind}", maximumAttempts: 1));

        (await processor.RunAutomaticAsync()).ShouldBe(1);

        await using var db = await factory.CreateDbContextAsync();
        (await db.AnalysisJobs.SingleAsync()).Error.ShouldStartWith(classification + ":");
    }

    [Fact]
    public async Task Retryable_failure_returns_to_the_durable_queue_with_backoff()
    {
        await using var factory = await CreateFactoryAsync();
        var handler = new Handler((_, _) => throw new HttpRequestException("offline"));
        await using var processor = new DurableAnalysisJobProcessor(
            factory, handler, new(1, 1, RetryBaseDelay: TimeSpan.FromMinutes(1)));
        await processor.EnqueueAsync(NewJob("retryable", maximumAttempts: 2));

        (await processor.RunAutomaticAsync()).ShouldBe(1);

        await using var db = await factory.CreateDbContextAsync();
        var persisted = await db.AnalysisJobs.Include(x => x.AttemptHistory).Include(x => x.Screenshot).SingleAsync();
        persisted.JobStatus.ShouldBe(AnalysisJobStatus.Pending);
        persisted.NextAttemptAt.ShouldNotBeNull();
        persisted.NextAttemptAt.Value.ShouldBeGreaterThan(DateTimeOffset.UtcNow);
        persisted.CompletedAt.ShouldBeNull();
        persisted.Screenshot!.ProcessingStatus.ShouldBe(ScreenshotStatus.Queued);
        persisted.AttemptHistory.Single().RetryAt.ShouldBe(persisted.NextAttemptAt);
    }

    [Fact]
    public async Task Every_trigger_drains_only_its_own_durable_jobs()
    {
        await using var factory = await CreateFactoryAsync();
        var handler = new Handler((job, _) => Task.FromResult(Result(job.Trigger.ToString())));
        await using var processor = new DurableAnalysisJobProcessor(factory, handler, new(1, 1));
        foreach (var trigger in Enum.GetValues<AnalysisJobTrigger>())
            await processor.EnqueueAsync(NewJob($"trigger-{trigger}", trigger: trigger));

        (await processor.RunManualAsync()).ShouldBe(1);
        (await processor.RunBatchAsync()).ShouldBe(1);
        (await processor.RunSessionEndAsync()).ShouldBe(1);
        (await processor.RunAutomaticAsync()).ShouldBe(1);

        await using var db = await factory.CreateDbContextAsync();
        (await db.AnalysisJobs.CountAsync(x => x.JobStatus == AnalysisJobStatus.Completed)).ShouldBe(4);
    }

    [Fact]
    public async Task Enqueue_requires_a_job_and_a_stable_idempotency_key()
    {
        await using var factory = await CreateFactoryAsync();
        await using var processor = new DurableAnalysisJobProcessor(
            factory, new Handler((_, _) => Task.FromResult(Result())));

        await Should.ThrowAsync<ArgumentNullException>(() => processor.EnqueueAsync(null!));
        await Should.ThrowAsync<ArgumentException>(() => processor.EnqueueAsync(NewJob(" ")));
    }

    private AnalysisJob NewJob(
        string key,
        int provider = 0,
        int maximumAttempts = 3,
        AnalysisJobTrigger trigger = AnalysisJobTrigger.Automatic) => new()
        {
            IdempotencyKey = key,
            ScreenshotId = _screenshotId,
            ProviderProfileId = _providerIds[provider],
            MaximumAttempts = maximumAttempts,
            Trigger = trigger
        };
    private static CaptureAnalysis Result(string text = "ok") => new() { ExtractedText = text };

    private async Task<Factory> CreateFactoryAsync()
    {
        Directory.CreateDirectory(_root);
        var factory = new Factory(Path.Combine(_root, "jobs.db"));
        await using var db = await factory.CreateDbContextAsync();
        await new DatabaseMigrationService(db).MigrateAsync();
        var session = new NoteSession();
        var screenshot = new Screenshot { Session = session };
        var providers = Enumerable.Range(0, 4).Select(i => new ProviderProfile { Name = $"provider-{i}", Provider = "test", Model = "test" }).ToArray();
        db.Screenshots.Add(screenshot);
        db.ProviderProfiles.AddRange(providers);
        await db.SaveChangesAsync();
        _screenshotId = screenshot.Id;
        _providerIds = providers.Select(x => x.Id).ToArray();
        return factory;
    }

    public ValueTask DisposeAsync() { if (Directory.Exists(_root)) Directory.Delete(_root, true); return ValueTask.CompletedTask; }

    private sealed class Factory(string path) : IDbContextFactory<VisualNotesDbContext>, IAsyncDisposable
    {
        public VisualNotesDbContext CreateDbContext() => new(new DbContextOptionsBuilder<VisualNotesDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options);
        public Task<VisualNotesDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Handler(Func<AnalysisJob, CancellationToken, Task<CaptureAnalysis>> action) : IAnalysisJobHandler
    {
        private int _active;
        private readonly ConcurrentDictionary<Guid, int> _providerActive = new();
        public int MaximumGlobal { get; private set; }
        public ConcurrentDictionary<Guid, int> MaximumByProvider { get; } = new();
        public ConcurrentDictionary<Guid, int> Executions { get; } = new();
        public async Task<CaptureAnalysis> ExecuteAsync(AnalysisJob job, CancellationToken cancellationToken)
        {
            Executions.AddOrUpdate(job.Id, 1, (_, value) => value + 1);
            var global = Interlocked.Increment(ref _active);
            MaximumGlobal = Math.Max(MaximumGlobal, global);
            var provider = job.ProviderProfileId ?? Guid.Empty;
            var activeProvider = _providerActive.AddOrUpdate(provider, 1, (_, value) => value + 1);
            MaximumByProvider.AddOrUpdate(provider, activeProvider, (_, value) => Math.Max(value, activeProvider));
            try { return await action(job, cancellationToken); }
            finally { Interlocked.Decrement(ref _active); _providerActive.AddOrUpdate(provider, 0, (_, value) => value - 1); }
        }
    }
}
