using Microsoft.EntityFrameworkCore;

using Shouldly;

using VisualNotes.Core.Models;
using VisualNotes.Infrastructure.Persistence;
using VisualNotes.Testing.Fixtures;
using VisualNotes.Testing.Infrastructure;
using VisualNotes.Testing.Utilities;

using Xunit;

namespace VisualNotes.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class PersistenceTests : IAsyncLifetime
{
    private readonly TemporarySqliteFactory _database = new();
    private VisualNotesDbContext _db = null!;

    public async Task InitializeAsync() => _db = await _database.CreateContextAsync().WaitAsync(TimeSpan.FromSeconds(20));

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _database.AssertNoOpenConnections();
        var root = _database.RootPath;
        await _database.DisposeAsync();
        TestGuards.AssertNoTemporaryFiles(root);
    }

    [Fact]
    public async Task Repository_round_trips_session_and_image_metadata_without_blob()
    {
        var session = TestData.Session();
        var shot = TestData.Capture(session.Id);
        session.Screenshots.Add(shot);
        await new SessionRepository(_db).AddAsync(session).CompletesWithin(TimeSpan.FromSeconds(10));
        await new UnitOfWork(_db).SaveChangesAsync().CompletesWithin(TimeSpan.FromSeconds(10));
        _db.ChangeTracker.Clear();

        var loaded = await new SessionRepository(_db).GetAsync(session.Id).WaitAsync(TimeSpan.FromSeconds(10));
        loaded.ShouldNotBeNull().Screenshots.Single().Image.ShouldNotBeNull().RelativePath.ShouldBe("sessions/fixture/captures/slide.png");
        var columns = await _db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('ScreenshotImages')").ToListAsync().WaitAsync(TimeSpan.FromSeconds(10));
        columns.ShouldNotContain(x => x.Contains("Data", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Restart_does_not_lose_partially_saved_capture_context()
    {
        var session = TestData.Session();
        var capture = TestData.Capture(session.Id);
        capture.UserContext = "explicación parcial antes del reinicio";
        session.Screenshots.Add(capture);
        await new SessionRepository(_db).AddAsync(session);
        await new UnitOfWork(_db).SaveChangesAsync();

        // Clearing the tracker simulates constructing a fresh runtime after shutdown.
        _db.ChangeTracker.Clear();
        var restored = await new ScreenshotRepository(_db).GetAsync(capture.Id);

        restored.ShouldNotBeNull().UserContext.ShouldBe("explicación parcial antes del reinicio");
    }

    [Fact]
    public async Task Course_module_session_hierarchy_and_capture_title_round_trip()
    {
        var course = new Course { Name = "Matemáticas" };
        var module = new CourseModule { Course = course, CourseId = course.Id, Name = "Álgebra", Order = 0 };
        course.Modules.Add(module);
        var session = TestData.Session();
        session.Course = course;
        session.CourseId = course.Id;
        session.CourseModule = module;
        session.CourseModuleId = module.Id;
        var capture = TestData.Capture(session.Id);
        capture.DisplayTitle = "Transformaciones lineales";
        session.Screenshots.Add(capture);

        await new SessionRepository(_db).AddAsync(session);
        await new UnitOfWork(_db).SaveChangesAsync();
        _db.ChangeTracker.Clear();

        var loaded = (await new SessionRepository(_db).GetAsync(session.Id)).ShouldNotBeNull();
        loaded.Course.ShouldNotBeNull().Name.ShouldBe("Matemáticas");
        loaded.CourseModule.ShouldNotBeNull().Name.ShouldBe("Álgebra");
        loaded.Screenshots.Single().DisplayTitle.ShouldBe("Transformaciones lineales");
    }
}
