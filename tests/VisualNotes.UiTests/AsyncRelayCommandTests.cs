using Shouldly;
using VisualNotes.App.ViewModels;

namespace VisualNotes.UiTests;

public sealed class AsyncRelayCommandTests
{
    [Fact]
    public async Task Prevents_reentry_while_execution_is_active()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executions = 0;
        var command = new AsyncRelayCommand(async (_, _) => { executions++; started.SetResult(); await release.Task; });
        var first = command.ExecuteAsync();
        await started.Task;
        await command.ExecuteAsync();
        executions.ShouldBe(1);
        command.IsRunning.ShouldBeTrue();
        command.CanExecute(null).ShouldBeFalse();
        release.SetResult();
        await first;
    }

    [Fact]
    public async Task Cancel_signals_token_and_restores_state()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var command = new AsyncRelayCommand(async (_, token) => { started.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); });
        var execution = command.ExecuteAsync();
        await started.Task;
        command.Cancel();
        await execution;
        command.IsRunning.ShouldBeFalse();
        command.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task Reports_errors_and_restores_CanExecute_after_failure()
    {
        var notifications = new RecordingNotifications();
        var changes = 0;
        var command = new AsyncRelayCommand((_, _) => throw new InvalidOperationException("fallo esperado"), notifications: notifications);
        command.CanExecuteChanged += (_, _) => changes++;
        await command.ExecuteAsync();
        notifications.Message.ShouldBe("fallo esperado");
        command.IsRunning.ShouldBeFalse();
        command.CanExecute(null).ShouldBeTrue();
        changes.ShouldBeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Restores_CanExecute_after_success()
    {
        var command = new AsyncRelayCommand((_, _) => Task.CompletedTask);
        await command.ExecuteAsync();
        command.IsRunning.ShouldBeFalse();
        command.CanExecute(null).ShouldBeTrue();
    }

    private sealed class RecordingNotifications : INotificationService
    {
        public string? Message { get; private set; }
        public void ShowError(string message) => Message = message;
    }
}
