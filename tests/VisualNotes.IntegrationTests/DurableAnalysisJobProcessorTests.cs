using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using VisualNotes.Core.Models;
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

    private AnalysisJob NewJob(string key, int provider = 0) => new() { IdempotencyKey = key, ScreenshotId = _screenshotId, ProviderProfileId = _providerIds[provider] };
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
