using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using VisualNotes.Core.Models;
using VisualNotes.Infrastructure.Persistence;

namespace VisualNotes.Infrastructure.Processing;

public sealed record AnalysisJobProcessorOptions(
    int GlobalConcurrency = 4,
    int ProviderConcurrency = 2,
    TimeSpan? LeaseDuration = null,
    TimeSpan? RetryBaseDelay = null)
{
    public TimeSpan EffectiveLeaseDuration => LeaseDuration ?? TimeSpan.FromMinutes(5);
    public TimeSpan EffectiveRetryBaseDelay => RetryBaseDelay ?? TimeSpan.FromSeconds(5);
}

public interface IAnalysisJobHandler
{
    Task<CaptureAnalysis> ExecuteAsync(AnalysisJob job, CancellationToken cancellationToken);
}

/// <summary>A persistent, lease-based processor. Database state is the queue, so restarting never loses work.</summary>
public sealed class DurableAnalysisJobProcessor : IAsyncDisposable
{
    private readonly IDbContextFactory<VisualNotesDbContext> _contexts;
    private readonly IAnalysisJobHandler _handler;
    private readonly AnalysisJobProcessorOptions _options;
    private readonly SemaphoreSlim _global;
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _providers = new();
    private readonly string _workerId = Guid.NewGuid().ToString("N");
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();

    public DurableAnalysisJobProcessor(IDbContextFactory<VisualNotesDbContext> contexts, IAnalysisJobHandler handler, AnalysisJobProcessorOptions? options = null)
    {
        _contexts = contexts;
        _handler = handler;
        _options = options ?? new();
        if (_options.GlobalConcurrency < 1 || _options.ProviderConcurrency < 1) throw new ArgumentOutOfRangeException(nameof(options));
        _global = new(_options.GlobalConcurrency, _options.GlobalConcurrency);
    }

    public async Task<AnalysisJob> EnqueueAsync(AnalysisJob job, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (string.IsNullOrWhiteSpace(job.IdempotencyKey)) throw new ArgumentException("An idempotency key is required.", nameof(job));
        await using var db = await _contexts.CreateDbContextAsync(cancellationToken);
        var existing = await db.AnalysisJobs.AsNoTracking().SingleOrDefaultAsync(x => x.IdempotencyKey == job.IdempotencyKey, cancellationToken);
        if (existing is not null) return existing;
        db.AnalysisJobs.Add(job);
        var screenshot = await db.Screenshots.SingleOrDefaultAsync(x => x.Id == job.ScreenshotId, cancellationToken);
        if (screenshot is not null) screenshot.ProcessingStatus = ScreenshotStatus.Queued;
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException)
        {
            db.Entry(job).State = EntityState.Detached;
            return await db.AnalysisJobs.AsNoTracking().SingleAsync(x => x.IdempotencyKey == job.IdempotencyKey, cancellationToken);
        }
        return job;
    }

    public Task<int> RunAutomaticAsync(CancellationToken cancellationToken = default) => DrainAsync([AnalysisJobTrigger.Automatic], cancellationToken);
    public Task<int> RunManualAsync(CancellationToken cancellationToken = default) => DrainAsync([AnalysisJobTrigger.Manual], cancellationToken);
    public Task<int> RunBatchAsync(CancellationToken cancellationToken = default) => DrainAsync([AnalysisJobTrigger.Batch], cancellationToken);
    public Task<int> RunSessionEndAsync(CancellationToken cancellationToken = default) => DrainAsync([AnalysisJobTrigger.SessionEnd], cancellationToken);

    public async Task<int> DrainAsync(IReadOnlyCollection<AnalysisJobTrigger> triggers, CancellationToken cancellationToken = default)
    {
        var count = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            var claimed = await ClaimAsync(triggers, cancellationToken);
            if (claimed.Count == 0) break;
            await Task.WhenAll(claimed.Select(x => ExecuteClaimedAsync(x, cancellationToken)));
            count += claimed.Count;
        }
        return count;
    }

    public async Task CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        if (_running.TryGetValue(jobId, out var source)) source.Cancel();
        await using var db = await _contexts.CreateDbContextAsync(cancellationToken);
        var job = await db.AnalysisJobs.SingleAsync(x => x.Id == jobId, cancellationToken);
        job.Cancel(DateTimeOffset.UtcNow);
        (await db.Screenshots.SingleAsync(x => x.Id == job.ScreenshotId, cancellationToken)).ProcessingStatus = ScreenshotStatus.Captured;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RetryAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        await using var db = await _contexts.CreateDbContextAsync(cancellationToken);
        var job = await db.AnalysisJobs.SingleAsync(x => x.Id == jobId, cancellationToken);
        job.Retry(DateTimeOffset.UtcNow);
        (await db.Screenshots.SingleAsync(x => x.Id == job.ScreenshotId, cancellationToken)).ProcessingStatus = ScreenshotStatus.Queued;
        await db.SaveChangesAsync(cancellationToken);
    }
    public async Task ChangeProviderAsync(Guid jobId, Guid providerId, CancellationToken cancellationToken = default) => await MutateAsync(jobId, x => x.ChangeProvider(providerId), cancellationToken);

    private async Task MutateAsync(Guid id, Action<AnalysisJob> action, CancellationToken token)
    {
        await using var db = await _contexts.CreateDbContextAsync(token);
        var job = await db.AnalysisJobs.SingleAsync(x => x.Id == id, token);
        action(job);
        await db.SaveChangesAsync(token);
    }

    private async Task<List<AnalysisJob>> ClaimAsync(IReadOnlyCollection<AnalysisJobTrigger> triggers, CancellationToken token)
    {
        await using var db = await _contexts.CreateDbContextAsync(token);
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, token);
        var now = DateTimeOffset.UtcNow;
        var jobs = await db.AnalysisJobs
            .Where(x => triggers.Contains(x.Trigger) &&
                ((x.JobStatus == AnalysisJobStatus.Pending && (x.NextAttemptAt == null || x.NextAttemptAt <= now)) ||
                 (x.JobStatus == AnalysisJobStatus.Running && x.LeaseExpiresAt < now)))
            .OrderBy(x => x.CreatedAt).Take(_options.GlobalConcurrency).ToListAsync(token);
        foreach (var job in jobs)
        {
            job.JobStatus = AnalysisJobStatus.Running;
            job.StartedAt ??= now;
            job.LeaseOwner = _workerId;
            job.LeaseExpiresAt = now + _options.EffectiveLeaseDuration;
            var screenshot = await db.Screenshots.SingleAsync(x => x.Id == job.ScreenshotId, token);
            screenshot.ProcessingStatus = ScreenshotStatus.Analyzing;
        }
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return jobs;
    }

    private async Task ExecuteClaimedAsync(AnalysisJob snapshot, CancellationToken outerToken)
    {
        await _global.WaitAsync(outerToken);
        var provider = _providers.GetOrAdd(snapshot.ProviderProfileId ?? Guid.Empty, _ => new(_options.ProviderConcurrency, _options.ProviderConcurrency));
        await provider.WaitAsync(outerToken);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(outerToken, _stopping.Token);
        _running[snapshot.Id] = linked;
        try
        {
            await RecordAttemptStartAsync(snapshot.Id, linked.Token);
            var result = await _handler.ExecuteAsync(snapshot, linked.Token);
            await CompleteAsync(snapshot.Id, result, linked.Token);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { await ReleaseAfterStopAsync(snapshot.Id); }
        catch (Exception exception) { await FailAsync(snapshot.Id, exception); }
        finally
        {
            _running.TryRemove(snapshot.Id, out _);
            provider.Release();
            _global.Release();
        }
    }

    private async Task RecordAttemptStartAsync(Guid id, CancellationToken token)
    {
        await using var db = await _contexts.CreateDbContextAsync(token);
        var job = await db.AnalysisJobs.SingleAsync(x => x.Id == id, token);
        if (job.JobStatus != AnalysisJobStatus.Running || job.LeaseOwner != _workerId) return;
        job.Attempts++;
        db.AnalysisJobAttempts.Add(new() { AnalysisJobId = id, AttemptNumber = job.Attempts, ProviderProfileId = job.ProviderProfileId, StartedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync(token);
    }

    private async Task CompleteAsync(Guid id, CaptureAnalysis result, CancellationToken token)
    {
        await using var db = await _contexts.CreateDbContextAsync(token);
        var job = await db.AnalysisJobs.Include(x => x.Result).SingleAsync(x => x.Id == id, token);
        if (job.JobStatus != AnalysisJobStatus.Running || job.LeaseOwner != _workerId || job.Result is not null) return;
        result.AnalysisJobId = id;
        job.Result = result;
        job.JobStatus = AnalysisJobStatus.Completed;
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.ClearLease();
        (await db.Screenshots.SingleAsync(x => x.Id == job.ScreenshotId, token)).ProcessingStatus = ScreenshotStatus.Ready;
        var attempt = await db.AnalysisJobAttempts.SingleAsync(x => x.AnalysisJobId == id && x.AttemptNumber == job.Attempts, token);
        attempt.FinishedAt = job.CompletedAt;
        await db.SaveChangesAsync(token);
    }

    private async Task FailAsync(Guid id, Exception error)
    {
        await using var db = await _contexts.CreateDbContextAsync();
        var job = await db.AnalysisJobs.SingleAsync(x => x.Id == id);
        if (job.JobStatus != AnalysisJobStatus.Running || job.LeaseOwner != _workerId) return;
        var now = DateTimeOffset.UtcNow;
        job.Error = $"{Classify(error)}: {error.Message}";
        job.JobStatus = job.Attempts < job.MaximumAttempts ? AnalysisJobStatus.Pending : AnalysisJobStatus.Failed;
        job.NextAttemptAt = job.JobStatus == AnalysisJobStatus.Pending ? now + TimeSpan.FromTicks(_options.EffectiveRetryBaseDelay.Ticks * (1L << Math.Min(job.Attempts - 1, 10))) : null;
        job.CompletedAt = job.JobStatus == AnalysisJobStatus.Failed ? now : null;
        job.ClearLease();
        (await db.Screenshots.SingleAsync(x => x.Id == job.ScreenshotId)).ProcessingStatus =
            job.JobStatus == AnalysisJobStatus.Pending ? ScreenshotStatus.Queued : ScreenshotStatus.Failed;
        var attempt = await db.AnalysisJobAttempts.SingleAsync(x => x.AnalysisJobId == id && x.AttemptNumber == job.Attempts);
        attempt.FinishedAt = now; attempt.Error = error.ToString(); attempt.RetryAt = job.NextAttemptAt;
        await db.SaveChangesAsync();
    }

    private static string Classify(Exception error) => error switch
    {
        LanguageModelException { Kind: LanguageModelErrorKind.Authentication } => "authentication",
        LanguageModelException { Kind: LanguageModelErrorKind.RateLimited or LanguageModelErrorKind.BudgetExhausted } => "limit",
        LanguageModelException { Kind: LanguageModelErrorKind.Timeout or LanguageModelErrorKind.ServiceUnavailable } or HttpRequestException => "network",
        LanguageModelException { Kind: LanguageModelErrorKind.InvalidResponse } or AnalysisResponseValidationException => "invalid-response",
        LanguageModelException { Kind: LanguageModelErrorKind.InvalidRequest } => "configuration",
        _ => "unknown"
    };

    private async Task ReleaseAfterStopAsync(Guid id)
    {
        await using var db = await _contexts.CreateDbContextAsync();
        var job = await db.AnalysisJobs.SingleAsync(x => x.Id == id);
        if (job.JobStatus == AnalysisJobStatus.Running && job.LeaseOwner == _workerId)
        {
            job.JobStatus = AnalysisJobStatus.Pending; job.NextAttemptAt = DateTimeOffset.UtcNow; job.ClearLease();
            var attempt = await db.AnalysisJobAttempts.SingleOrDefaultAsync(x => x.AnalysisJobId == id && x.AttemptNumber == job.Attempts);
            if (attempt is not null) { attempt.FinishedAt = DateTimeOffset.UtcNow; attempt.Error = "Processor stopped"; attempt.RetryAt = job.NextAttemptAt; }
            await db.SaveChangesAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stopping.Cancel();
        while (!_running.IsEmpty) await Task.Delay(10);
        _stopping.Dispose(); _global.Dispose();
        foreach (var semaphore in _providers.Values) semaphore.Dispose();
    }
}
