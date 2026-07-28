using VisualNotes.Core.Models;

namespace VisualNotes.Core.Services;

/// <summary>Coordinates session changes and persists every successful operation immediately.</summary>
public sealed class SessionCoordinator(ISessionRepository sessions, IScreenshotRepository screenshots, ISettingsRepository settings, IUnitOfWork unitOfWork)
{
    public const string LastOpenSessionKey = "session.last-open";

    public async Task<NoteSession> CreateAsync(NoteSession draft, Guid? duplicateFrom = null, CancellationToken ct = default)
    {
        if (duplicateFrom is not null)
        {
            var source = await sessions.GetAsync(duplicateFrom.Value, ct) ?? throw new InvalidOperationException("La sesión que se desea duplicar no existe.");
            CopyConfiguration(source, draft);
            foreach (var section in source.Sections.OrderBy(x => x.Order))
                draft.Sections.Add(new NoteSection { Title = section.Title, Description = section.Description, Content = section.Content, Order = section.Order });
        }
        await sessions.AddAsync(draft, ct);
        await SaveAndRememberAsync(draft.Id, ct);
        return draft;
    }

    public async Task<NoteSession?> RestoreLastOpenAsync(CancellationToken ct = default)
    {
        var id = await settings.GetAsync<Guid?>(LastOpenSessionKey, ct);
        return id is null ? null : await sessions.GetAsync(id.Value, ct);
    }

    public Task ContinueAsync(NoteSession session, CancellationToken ct = default) => SetPausedAsync(session, false, ct);
    public Task PauseAsync(NoteSession session, CancellationToken ct = default) => SetPausedAsync(session, true, ct);

    public async Task SetPausedAsync(NoteSession session, bool paused, CancellationToken ct = default)
    {
        session.IsPaused = paused;
        await SaveAndRememberAsync(session.Id, ct);
    }

    public async Task<NoteSection> AddSectionAsync(NoteSession session, string title, string description = "", Guid? parentId = null, CancellationToken ct = default)
    {
        if (parentId is not null && session.Sections.All(x => x.Id != parentId)) throw new ArgumentException("La sección padre no pertenece a la sesión.", nameof(parentId));
        var section = new NoteSection { SessionId = session.Id, Title = title, Description = description, ParentSectionId = parentId, Order = session.Sections.Count };
        session.Sections.Add(section);
        session.ActiveSectionId = section.Id;
        await SaveAndRememberAsync(session.Id, ct);
        return section;
    }

    public async Task RenameSectionAsync(NoteSection section, string title, CancellationToken ct = default)
    {
        section.Title = title;
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task ActivateSectionAsync(NoteSession session, Guid sectionId, CancellationToken ct = default)
    {
        if (session.Sections.All(x => x.Id != sectionId)) throw new ArgumentException("La sección no pertenece a la sesión.", nameof(sectionId));
        session.ActiveSectionId = sectionId;
        await SaveAndRememberAsync(session.Id, ct);
    }

    public async Task ReorderSectionAsync(NoteSession session, Guid sectionId, int newOrder, CancellationToken ct = default)
    {
        var ordered = session.Sections.OrderBy(x => x.Order).ToList();
        var section = ordered.Single(x => x.Id == sectionId);
        ordered.Remove(section);
        ordered.Insert(Math.Clamp(newOrder, 0, ordered.Count), section);
        for (var index = 0; index < ordered.Count; index++) ordered[index].Order = index;
        await SaveAndRememberAsync(session.Id, ct);
    }

    public async Task AddCaptureAsync(NoteSession session, Screenshot capture, CancellationToken ct = default)
    {
        if (session.IsPaused) throw new InvalidOperationException("La sesión está pausada. Reanúdala antes de capturar.");
        capture.SessionId = session.Id;
        capture.SectionId = session.ActiveSectionId;
        await screenshots.AddAsync(capture, ct);
        await SaveAndRememberAsync(session.Id, ct);
    }

    public async Task MoveCapturesAsync(NoteSession session, IEnumerable<Screenshot> captures, Guid sectionId, CancellationToken ct = default)
    {
        if (session.Sections.All(x => x.Id != sectionId)) throw new ArgumentException("La sección no pertenece a la sesión.", nameof(sectionId));
        foreach (var capture in captures)
        {
            if (capture.SessionId != session.Id) throw new ArgumentException("Una captura no pertenece a la sesión.", nameof(captures));
            capture.SectionId = sectionId;
        }
        await SaveAndRememberAsync(session.Id, ct);
    }

    public async Task SaveCaptureMetadataAsync(Screenshot capture, CancellationToken ct = default)
    {
        capture.Revisions.Add(new CaptureRevision
        {
            ScreenshotId = capture.Id,
            RevisionNumber = capture.Revisions.Count + 1,
            UserContext = capture.UserContext,
            CaptureInstruction = capture.CaptureInstruction,
            Tags = capture.Tags,
            Importance = capture.Importance,
            IncludeInDocument = capture.IncludeInDocument
        });
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task ReanalyzeAsync(Screenshot capture, CancellationToken ct = default)
    {
        capture.ProcessingStatus = ScreenshotStatus.Queued;
        await unitOfWork.SaveChangesAsync(ct);
    }

    public async Task RegenerateNoteAsync(Screenshot capture, CancellationToken ct = default)
    {
        capture.ProcessingStatus = ScreenshotStatus.NeedsReview;
        await unitOfWork.SaveChangesAsync(ct);
    }

    private async Task SaveAndRememberAsync(Guid sessionId, CancellationToken ct)
    {
        await settings.SetAsync(LastOpenSessionKey, sessionId, cancellationToken: ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    private static void CopyConfiguration(NoteSession source, NoteSession target)
    {
        target.CourseId = source.CourseId; target.Name = source.Name + " (copia)"; target.Module = source.Module;
        target.Topic = source.Topic; target.Professor = source.Professor; target.Language = source.Language;
        target.WorkingFolder = source.WorkingFolder; target.PlannedDocumentName = source.PlannedDocumentName;
        target.InstructionTemplate = source.InstructionTemplate;
    }
}
