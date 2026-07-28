using VisualNotes.App.ViewModels;
using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

namespace VisualNotes.App.Services;

/// <summary>Owns the session-scoped target and undo history for post-capture actions.</summary>
public sealed class CaptureActionCoordinator(
    ICaptureWorkspace workspace,
    Func<NoteSession?> activeSession,
    Action<Screenshot> openContext,
    Action<string> notify) : ICaptureActionContract
{
    private readonly Dictionary<Guid, Screenshot> _lastCaptureBySession = [];
    private readonly Dictionary<Guid, Stack<CaptureOperation>> _historyBySession = [];

    public bool CanUndo
    {
        get
        {
            if (!IsSessionAvailable(out var session) || History(session.Id).TryPeek(out var operation) == false) return false;
            return operation.Capture.SessionId == session.Id && operation.Capture.Status != EntityStatus.Deleted;
        }
    }
    public bool CanMarkImportant => TryGetApplicableCapture(out var capture) && capture.Importance == CaptureImportance.Normal;
    public bool CanAddContext => TryGetApplicableCapture(out _);
    public event EventHandler? CanExecuteChanged;

    public void CaptureCompleted(Screenshot capture)
    {
        _lastCaptureBySession[capture.SessionId] = capture;
        History(capture.SessionId).Push(new(CaptureOperationKind.Created, capture, capture.Importance));
        RaiseCanExecuteChanged();
    }

    public void SessionStateChanged() => RaiseCanExecuteChanged();

    public async Task MarkImportantAsync()
    {
        if (!ValidateTarget(out var session, out var capture)) return;
        if (capture.Importance != CaptureImportance.Normal) return;
        var previous = capture.Importance;
        capture.Importance = CaptureImportance.Important;
        await workspace.SaveAsync([capture]);
        History(session.Id).Push(new(CaptureOperationKind.Importance, capture, previous));
        RaiseCanExecuteChanged();
    }

    public Task AddContextAsync()
    {
        if (ValidateTarget(out _, out var capture)) openContext(capture);
        return Task.CompletedTask;
    }

    public async Task UndoAsync()
    {
        if (!IsSessionAvailable(out var session)) return;
        var history = History(session.Id);
        if (history.Count == 0) return;
        var operation = history.Peek();
        if (!Validate(operation.Capture, session)) return;

        if (operation.Kind == CaptureOperationKind.Created)
            operation.Capture.Status = EntityStatus.Deleted;
        else
            operation.Capture.Importance = operation.PreviousImportance;

        await workspace.SaveAsync([operation.Capture]);
        history.Pop();
        RaiseCanExecuteChanged();
    }

    private bool TryGetApplicableCapture(out Screenshot capture)
    {
        capture = null!;
        return IsSessionAvailable(out var session)
            && _lastCaptureBySession.TryGetValue(session.Id, out capture!)
            && capture.SessionId == session.Id
            && capture.Status != EntityStatus.Deleted;
    }

    private bool ValidateTarget(out NoteSession session, out Screenshot capture)
    {
        capture = null!;
        if (!IsSessionAvailable(out session)) return false;
        if (!_lastCaptureBySession.TryGetValue(session.Id, out capture!)) return false;
        return Validate(capture, session);
    }

    private bool Validate(Screenshot capture, NoteSession session)
    {
        if (capture.SessionId != session.Id)
        {
            notify("La última captura pertenece a otra sesión.");
            return false;
        }
        if (capture.Status == EntityStatus.Deleted)
        {
            notify("La captura ya fue eliminada.");
            RaiseCanExecuteChanged();
            return false;
        }
        return true;
    }

    private bool IsSessionAvailable(out NoteSession session)
    {
        session = activeSession()!;
        return session is not null && !session.IsPaused;
    }

    private Stack<CaptureOperation> History(Guid sessionId) =>
        _historyBySession.TryGetValue(sessionId, out var history)
            ? history
            : _historyBySession[sessionId] = new();

    private void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    private enum CaptureOperationKind { Created, Importance }
    private sealed record CaptureOperation(CaptureOperationKind Kind, Screenshot Capture, CaptureImportance PreviousImportance);
}
