using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

namespace VisualNotes.Infrastructure.Persistence;

public sealed class SessionRepository(VisualNotesDbContext db) : ISessionRepository
{
    public Task<NoteSession?> GetAsync(Guid id, CancellationToken ct = default) => db.Sessions.Include(x => x.Sections).Include(x => x.Screenshots).ThenInclude(x => x.Image).SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<NoteSession>> ListAsync(CancellationToken ct = default) =>
        (await db.Sessions.AsNoTracking().Include(x => x.Sections).Include(x => x.Screenshots).ToListAsync(ct))
        .OrderByDescending(x => x.ModifiedAt).ToList();
    public Task AddAsync(NoteSession session, CancellationToken ct = default) => db.Sessions.AddAsync(session, ct).AsTask();
    public void Remove(NoteSession session) => db.Sessions.Remove(session);
}

public sealed class ScreenshotRepository(VisualNotesDbContext db) : IScreenshotRepository
{
    public Task<Screenshot?> GetAsync(Guid id, CancellationToken ct = default) => db.Screenshots.Include(x => x.Image).Include(x => x.Context).Include(x => x.Revisions).Include(x => x.AnalysisJobs).ThenInclude(x => x.Result).SingleOrDefaultAsync(x => x.Id == id, ct);
    public async Task<IReadOnlyList<Screenshot>> ListBySessionAsync(Guid sessionId, CancellationToken ct = default) =>
        (await db.Screenshots.AsNoTracking().Include(x => x.Image).Include(x => x.AnalysisJobs).ThenInclude(x => x.Result).Where(x => x.SessionId == sessionId).ToListAsync(ct))
        .OrderBy(x => x.CapturedAt).ToList();
    public Task AddAsync(Screenshot screenshot, CancellationToken ct = default) => db.Screenshots.AddAsync(screenshot, ct).AsTask();
}

public sealed class AnalysisRepository(VisualNotesDbContext db) : IAnalysisRepository
{
    public Task<AnalysisJob?> GetJobAsync(Guid id, CancellationToken ct = default) => db.AnalysisJobs.Include(x => x.Result!).ThenInclude(x => x.Regions).SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task AddJobAsync(AnalysisJob job, CancellationToken ct = default) => db.AnalysisJobs.AddAsync(job, ct).AsTask();
}
public sealed class PromptProfileRepository(VisualNotesDbContext db) : IPromptProfileRepository
{
    public async Task<IReadOnlyList<PromptProfile>> ListAsync(CancellationToken ct = default) => await db.PromptProfiles.AsNoTracking().ToListAsync(ct);
    public Task AddAsync(PromptProfile profile, CancellationToken ct = default) => db.PromptProfiles.AddAsync(profile, ct).AsTask();
}
public sealed class ProviderProfileRepository(VisualNotesDbContext db) : IProviderProfileRepository
{
    public async Task<IReadOnlyList<ProviderProfile>> ListAsync(CancellationToken ct = default) => await db.ProviderProfiles.AsNoTracking().ToListAsync(ct);
    public Task AddAsync(ProviderProfile profile, CancellationToken ct = default) => db.ProviderProfiles.AddAsync(profile, ct).AsTask();
}
public sealed class SettingsRepository(VisualNotesDbContext db) : ISettingsRepository
{
    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default) { var item = await db.Settings.AsNoTracking().SingleOrDefaultAsync(x => x.Key == key, ct); return item is null ? default : JsonSerializer.Deserialize<T>(item.JsonValue); }
    public async Task SetAsync<T>(string key, T value, bool isSecret = false, CancellationToken ct = default) { if (isSecret) throw new InvalidOperationException("Secrets must be stored through IApiCredentialStore."); var item = await db.Settings.SingleOrDefaultAsync(x => x.Key == key, ct); if (item is null) { item = new AppSetting { Key = key }; await db.Settings.AddAsync(item, ct); } item.JsonValue = JsonSerializer.Serialize(value); item.IsSecret = false; }
}
public sealed class UnitOfWork(VisualNotesDbContext db) : IUnitOfWork { public Task<int> SaveChangesAsync(CancellationToken ct = default) => db.SaveChangesAsync(ct); }
