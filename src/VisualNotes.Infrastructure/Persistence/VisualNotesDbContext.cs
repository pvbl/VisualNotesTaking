using Microsoft.EntityFrameworkCore;

using VisualNotes.Core.Models;

namespace VisualNotes.Infrastructure.Persistence;

public sealed class VisualNotesDbContext(DbContextOptions<VisualNotesDbContext> options) : DbContext(options)
{
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<CourseModule> CourseModules => Set<CourseModule>();
    public DbSet<NoteSession> Sessions => Set<NoteSession>();
    public DbSet<NoteSection> Sections => Set<NoteSection>();
    public DbSet<Screenshot> Screenshots => Set<Screenshot>();
    public DbSet<ScreenshotImage> ScreenshotImages => Set<ScreenshotImage>();
    public DbSet<ScreenshotContext> ScreenshotContexts => Set<ScreenshotContext>();
    public DbSet<CaptureRevision> CaptureRevisions => Set<CaptureRevision>();
    public DbSet<AnalysisJob> AnalysisJobs => Set<AnalysisJob>();
    public DbSet<AnalysisJobAttempt> AnalysisJobAttempts => Set<AnalysisJobAttempt>();
    public DbSet<CaptureAnalysis> CaptureAnalyses => Set<CaptureAnalysis>();
    public DbSet<VisualRegion> VisualRegions => Set<VisualRegion>();
    public DbSet<GeneratedNote> GeneratedNotes => Set<GeneratedNote>();
    public DbSet<PromptProfile> PromptProfiles => Set<PromptProfile>();
    public DbSet<ProviderProfile> ProviderProfiles => Set<ProviderProfile>();
    public DbSet<ExportRecord> ExportRecords => Set<ExportRecord>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Ignore<CaptureMetadata>();
        modelBuilder.Ignore<CaptureRegion>();
        modelBuilder.Ignore<CapturedFrame>();
        modelBuilder.Ignore<ExtractedContent>();
        modelBuilder.Ignore<ExportDocument>();

        foreach (var entity in modelBuilder.Model.GetEntityTypes().Where(x => typeof(Entity).IsAssignableFrom(x.ClrType)))
        {
            modelBuilder.Entity(entity.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
            modelBuilder.Entity(entity.ClrType).Property(nameof(Entity.Version)).IsConcurrencyToken(false);
        }

        modelBuilder.Entity<NoteSession>().HasIndex(x => x.CourseId);
        modelBuilder.Entity<CourseModule>().HasIndex(x => new { x.CourseId, x.Name }).IsUnique();
        modelBuilder.Entity<CourseModule>().HasMany(x => x.Sessions).WithOne(x => x.CourseModule)
            .HasForeignKey(x => x.CourseModuleId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<NoteSession>().HasIndex(x => x.CourseModuleId);
        modelBuilder.Entity<NoteSection>().HasIndex(x => x.SessionId);
        modelBuilder.Entity<NoteSection>().HasOne(x => x.ParentSection).WithMany(x => x.Children).HasForeignKey(x => x.ParentSectionId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<NoteSession>().HasOne<NoteSection>().WithMany().HasForeignKey(x => x.ActiveSectionId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<Screenshot>().HasIndex(x => x.SessionId);
        modelBuilder.Entity<Screenshot>().HasIndex(x => x.SectionId);
        modelBuilder.Entity<Screenshot>().HasIndex(x => x.CapturedAt);
        modelBuilder.Entity<Screenshot>().HasIndex(x => x.ProcessingStatus);
        modelBuilder.Entity<Screenshot>().HasIndex(x => x.PerceptualHash);
        modelBuilder.Entity<Screenshot>().HasOne(x => x.Image).WithOne(x => x.Screenshot).HasForeignKey<ScreenshotImage>(x => x.ScreenshotId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<Screenshot>().HasOne(x => x.Context).WithOne(x => x.Screenshot).HasForeignKey<ScreenshotContext>(x => x.ScreenshotId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<CaptureRevision>().HasIndex(x => new { x.ScreenshotId, x.RevisionNumber }).IsUnique();
        modelBuilder.Entity<Screenshot>().HasMany(x => x.Revisions).WithOne(x => x.Screenshot).HasForeignKey(x => x.ScreenshotId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AnalysisJob>().HasOne(x => x.Result).WithOne(x => x.AnalysisJob).HasForeignKey<CaptureAnalysis>(x => x.AnalysisJobId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AnalysisJob>().HasIndex(x => x.IdempotencyKey).IsUnique();
        modelBuilder.Entity<AnalysisJob>().HasIndex(x => new { x.JobStatus, x.NextAttemptAt });
        modelBuilder.Entity<AnalysisJobAttempt>().HasIndex(x => new { x.AnalysisJobId, x.AttemptNumber }).IsUnique();
        modelBuilder.Entity<AnalysisJob>().HasMany(x => x.AttemptHistory).WithOne(x => x.AnalysisJob).HasForeignKey(x => x.AnalysisJobId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<AppSetting>().HasIndex(x => x.Key).IsUnique();
        modelBuilder.Entity<ScreenshotImage>().Property(x => x.RelativePath).HasMaxLength(1024);
        modelBuilder.Entity<VisualRegion>().Property(x => x.CropRelativePath).HasMaxLength(1024);
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added) { entry.Entity.CreatedAt = now; entry.Entity.ModifiedAt = now; entry.Entity.Version = 1; }
            else if (entry.State == EntityState.Modified) { entry.Entity.ModifiedAt = now; entry.Entity.Version++; }
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
