using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using VisualNotes.Infrastructure.Persistence;

namespace VisualNotes.Testing.Infrastructure;

public sealed class TemporarySqliteFactory : IAsyncDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "visualnotes-sqlite-" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _databases = [];
    private bool _disposed;

    public string RootPath => _root;

    public async Task<VisualNotesDbContext> CreateContextAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Directory.CreateDirectory(_root);
        var database = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".db");
        _databases.Add(database);
        var options = new DbContextOptionsBuilder<VisualNotesDbContext>()
            .UseSqlite($"Data Source={database};Pooling=False")
            .Options;
        var context = new VisualNotesDbContext(options);
        await new DatabaseMigrationService(context).MigrateAsync(cancellationToken);
        return context;
    }

    public void AssertNoOpenConnections()
    {
        SqliteConnection.ClearAllPools();
        foreach (var database in _databases.Where(File.Exists))
        {
            try { using var handle = File.Open(database, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException exception) { throw new InvalidOperationException($"An open SQLite connection still owns '{database}'.", exception); }
        }
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        SqliteConnection.ClearAllPools();
        _disposed = true;
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        return ValueTask.CompletedTask;
    }
}
