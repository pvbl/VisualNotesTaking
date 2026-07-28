using Shouldly;

using VisualNotes.Core.Models;

namespace VisualNotes.UnitTests;

public sealed class AnalysisJobStateTests
{
    [Fact]
    public void Failed_job_can_be_retried()
    {
        var now = DateTimeOffset.UtcNow;
        var job = new AnalysisJob { JobStatus = AnalysisJobStatus.Failed, Error = "timeout", CompletedAt = now };

        job.Retry(now.AddMinutes(1));

        job.JobStatus.ShouldBe(AnalysisJobStatus.Pending);
        job.Error.ShouldBeNull();
        job.NextAttemptAt.ShouldBe(now.AddMinutes(1));
        job.CompletedAt.ShouldBeNull();
    }

    [Fact]
    public void Pending_job_can_be_cancelled()
    {
        var now = DateTimeOffset.UtcNow;
        var job = new AnalysisJob { LeaseOwner = "worker", LeaseExpiresAt = now.AddMinutes(1) };

        job.Cancel(now);

        job.JobStatus.ShouldBe(AnalysisJobStatus.Cancelled);
        job.CompletedAt.ShouldBe(now);
        job.LeaseOwner.ShouldBeNull();
    }

    [Fact]
    public void Provider_can_only_change_before_execution()
    {
        var provider = Guid.NewGuid();
        var job = new AnalysisJob();
        job.ChangeProvider(provider);
        job.ProviderProfileId.ShouldBe(provider);

        job.JobStatus = AnalysisJobStatus.Running;
        Should.Throw<InvalidOperationException>(() => job.ChangeProvider(Guid.NewGuid()));
    }

    [Theory]
    [InlineData(AnalysisJobStatus.Pending)]
    [InlineData(AnalysisJobStatus.Running)]
    [InlineData(AnalysisJobStatus.Completed)]
    public void Invalid_retry_transitions_are_rejected(AnalysisJobStatus status)
    {
        var job = new AnalysisJob { JobStatus = status };
        Should.Throw<InvalidOperationException>(() => job.Retry(DateTimeOffset.UtcNow));
    }
}
