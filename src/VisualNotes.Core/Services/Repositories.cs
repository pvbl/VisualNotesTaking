using VisualNotes.Core.Models;

namespace VisualNotes.Core.Services;

public interface ISessionRepository
{
    Task<NoteSession?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<NoteSession>> ListAsync(CancellationToken cancellationToken = default);
    Task AddAsync(NoteSession session, CancellationToken cancellationToken = default);
    void Remove(NoteSession session);
}

public interface IScreenshotRepository
{
    Task<Screenshot?> GetAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Screenshot>> ListBySessionAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task AddAsync(Screenshot screenshot, CancellationToken cancellationToken = default);
}

public interface IAnalysisRepository
{
    Task<AnalysisJob?> GetJobAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddJobAsync(AnalysisJob job, CancellationToken cancellationToken = default);
}

public interface IPromptProfileRepository { Task<IReadOnlyList<PromptProfile>> ListAsync(CancellationToken cancellationToken = default); Task AddAsync(PromptProfile profile, CancellationToken cancellationToken = default); }
public interface IProviderProfileRepository { Task<IReadOnlyList<ProviderProfile>> ListAsync(CancellationToken cancellationToken = default); Task AddAsync(ProviderProfile profile, CancellationToken cancellationToken = default); }
public interface ISettingsRepository { Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default); Task SetAsync<T>(string key, T value, bool isSecret = false, CancellationToken cancellationToken = default); }
public interface IUnitOfWork { Task<int> SaveChangesAsync(CancellationToken cancellationToken = default); }
