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
}
