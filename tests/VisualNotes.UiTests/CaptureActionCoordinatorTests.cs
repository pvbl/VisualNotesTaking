using NSubstitute;

using Shouldly;

using VisualNotes.App.Services;
using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

namespace VisualNotes.UiTests;

public sealed class CaptureActionCoordinatorTests
{
    [Fact]
    public void Main_commands_expose_the_explicit_contract_CanExecute_state()
    {
        var actions = new StubActions { Enabled = false };
        var sessions = Substitute.For<ISessionRepository>();
        var coordinator = new SessionCoordinator(sessions, Substitute.For<IScreenshotRepository>(),
            Substitute.For<ISettingsRepository>(), Substitute.For<IUnitOfWork>());
        var main = new VisualNotes.App.ViewModels.MainViewModel(coordinator, sessions, captureActions: actions);

        main.UndoCommand.CanExecute(null).ShouldBeFalse();
        main.MarkImportantCommand.CanExecute(null).ShouldBeFalse();
        main.AddContextCommand.CanExecute(null).ShouldBeFalse();

        actions.Enabled = true;
        actions.RaiseChanged();
        main.UndoCommand.CanExecute(null).ShouldBeTrue();
        main.MarkImportantCommand.CanExecute(null).ShouldBeTrue();
        main.AddContextCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task Capture_can_resume_the_active_session_and_notifies_the_panel()
    {
        var actions = new StubActions();
        var sessions = Substitute.For<ISessionRepository>();
        var coordinator = new SessionCoordinator(sessions, Substitute.For<IScreenshotRepository>(),
            Substitute.For<ISettingsRepository>(), Substitute.For<IUnitOfWork>());
        var main = new VisualNotes.App.ViewModels.MainViewModel(coordinator, sessions, captureActions: actions);
        var session = new NoteSession { IsPaused = true };
        main.Sessions.SelectedSession = session;
        var activations = 0;
        main.SessionActivated += () => activations++;

        await main.ResumeActiveSessionAsync();

        session.IsPaused.ShouldBeFalse();
        activations.ShouldBe(1);
        actions.StateChangeCount.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Actions_are_disabled_without_capture_and_while_session_is_paused()
    {
        var session = new NoteSession();
        var sut = Create(session, out _, out _, out _);

        sut.CanUndo.ShouldBeFalse();
        sut.CanMarkImportant.ShouldBeFalse();
        sut.CanAddContext.ShouldBeFalse();

        sut.CaptureCompleted(new Screenshot { SessionId = session.Id });
        sut.CanUndo.ShouldBeTrue();
        sut.CanMarkImportant.ShouldBeTrue();
        sut.CanAddContext.ShouldBeTrue();

        session.IsPaused = true;
        sut.SessionStateChanged();
        sut.CanUndo.ShouldBeFalse();
        sut.CanMarkImportant.ShouldBeFalse();
        sut.CanAddContext.ShouldBeFalse();
    }

    [Fact]
    public async Task Important_and_undo_persist_the_changed_importance()
    {
        var session = new NoteSession();
        var capture = new Screenshot { SessionId = session.Id };
        var sut = Create(session, out var workspace, out _, out _);
        sut.CaptureCompleted(capture);

        await sut.MarkImportantAsync();
        capture.Importance.ShouldBe(CaptureImportance.Important);
        await workspace.Received(1).SaveAsync(Arg.Is<IReadOnlyCollection<Screenshot>>(items => items.Single() == capture), Arg.Any<CancellationToken>());

        await sut.UndoAsync();
        capture.Importance.ShouldBe(CaptureImportance.Normal);
        await workspace.Received(2).SaveAsync(Arg.Any<IReadOnlyCollection<Screenshot>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Undoing_a_completed_capture_soft_deletes_and_persists_it()
    {
        var session = new NoteSession();
        var capture = new Screenshot { SessionId = session.Id };
        var sut = Create(session, out var workspace, out _, out _);
        sut.CaptureCompleted(capture);

        await sut.UndoAsync();

        capture.Status.ShouldBe(EntityStatus.Deleted);
        await workspace.Received(1).SaveAsync(Arg.Is<IReadOnlyCollection<Screenshot>>(items => items.Single().Status == EntityStatus.Deleted), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Context_focuses_the_last_capture_and_stale_capture_reports_the_reason()
    {
        var session = new NoteSession();
        var capture = new Screenshot { SessionId = session.Id };
        var sut = Create(session, out _, out var opened, out var messages);
        sut.CaptureCompleted(capture);

        await sut.AddContextAsync();
        opened.Value.ShouldBe(capture);

        capture.Status = EntityStatus.Deleted;
        await sut.AddContextAsync();
        messages.ShouldContain("La captura ya fue eliminada.");
    }

    private static CaptureActionCoordinator Create(NoteSession session, out ICaptureWorkspace workspace,
        out Box<Screenshot?> opened, out List<string> messages)
    {
        workspace = Substitute.For<ICaptureWorkspace>();
        opened = new();
        messages = [];
        var capturedMessages = messages;
        var capturedOpened = opened;
        return new CaptureActionCoordinator(workspace, () => session, value => capturedOpened.Value = value, capturedMessages.Add);
    }

    private sealed class Box<T> { public T? Value { get; set; } }

    private sealed class StubActions : VisualNotes.App.ViewModels.ICaptureActionContract
    {
        public bool Enabled { get; set; }
        public int StateChangeCount { get; private set; }
        public bool CanUndo => Enabled;
        public bool CanMarkImportant => Enabled;
        public bool CanAddContext => Enabled;
        public event EventHandler? CanExecuteChanged;
        public Task UndoAsync() => Task.CompletedTask;
        public Task MarkImportantAsync() => Task.CompletedTask;
        public Task AddContextAsync() => Task.CompletedTask;
        public void CaptureCompleted(Screenshot capture) { }
        public void SessionStateChanged() => StateChangeCount++;
        public void RaiseChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
