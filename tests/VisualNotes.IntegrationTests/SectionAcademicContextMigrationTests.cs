using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Shouldly;

using VisualNotes.Core.Models;
using VisualNotes.Infrastructure.Persistence;
using VisualNotes.Testing.Utilities;

namespace VisualNotes.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class SectionAcademicContextMigrationTests
{
    [Fact]
    public async Task Legacy_session_context_is_copied_to_sections_without_losing_captures()
    {
        var root = Path.Combine(Path.GetTempPath(), "visualnotes-migration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "legacy.db");
        var options = new DbContextOptionsBuilder<VisualNotesDbContext>()
            .UseSqlite($"Data Source={path};Pooling=False")
            .Options;

        try
        {
            var courseId = Guid.NewGuid();
            var moduleId = Guid.NewGuid();
            var sessionId = Guid.NewGuid();
            var sectionId = Guid.NewGuid();
            var screenshotId = Guid.NewGuid();
            var now = DateTimeOffset.UtcNow;

            await using (var legacy = new VisualNotesDbContext(options))
            {
                await legacy.GetService<IMigrator>()
                    .MigrateAsync("20260728010000_CaptureReviewWorkspace")
                    .WaitAsync(TimeSpan.FromSeconds(20));
                var course = new Course { Id = courseId, Name = "Matemáticas" };
                var module = new CourseModule
                {
                    Id = moduleId, Course = course, CourseId = courseId, Name = "Álgebra"
                };
                course.Modules.Add(module);
                var session = new NoteSession
                {
                    Id = sessionId,
                    Course = course,
                    CourseId = courseId,
                    CourseModule = module,
                    CourseModuleId = moduleId,
                    Module = module.Name,
                    Name = "Sesión antigua"
                };
                legacy.Sessions.Add(session);
                await legacy.SaveChangesAsync();
                await legacy.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO Sections
                        (Id, SessionId, Title, Content, "Order", CreatedAt, ModifiedAt,
                         Version, Status, Description, ParentSectionId)
                    VALUES
                        ({sectionId}, {sessionId}, {"Matrices"}, {""}, {0}, {now}, {now},
                         {1}, {0}, {""}, {null});
                    """);
                legacy.Screenshots.Add(new Screenshot
                {
                    Id = screenshotId,
                    SessionId = sessionId,
                    SectionId = sectionId,
                    CapturedAt = now,
                    Width = 1280,
                    Height = 720
                });
                await legacy.SaveChangesAsync();
            }

            await using (var upgraded = new VisualNotesDbContext(options))
            {
                await new DatabaseMigrationService(upgraded).MigrateAsync()
                    .WaitAsync(TimeSpan.FromSeconds(20));
                var section = await upgraded.Sections.AsNoTracking()
                    .SingleAsync(item => item.Id == sectionId);

                section.CourseId.ShouldBe(courseId);
                section.CourseModuleId.ShouldBe(moduleId);
                (await upgraded.Screenshots.AsNoTracking()
                    .CountAsync(item => item.Id == screenshotId)).ShouldBe(1);
                (await upgraded.Sessions.AsNoTracking()
                    .CountAsync(item => item.Id == sessionId)).ShouldBe(1);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(root)) Directory.Delete(root, true);
            TestGuards.AssertNoTemporaryFiles(root);
        }
    }
}
