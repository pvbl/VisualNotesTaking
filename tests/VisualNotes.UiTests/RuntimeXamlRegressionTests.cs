using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

using NSubstitute;

using Shouldly;

using VisualNotes.App.ViewModels;
using VisualNotes.App.Views;
using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

namespace VisualNotes.UiTests;

[Collection(WindowsResourceCollection.Name)]
[Trait("Category", "UI")]
[Trait("Category", "Windows")]
public sealed class RuntimeXamlRegressionTests
{
    [Fact]
    public void Primary_views_create_their_bindings_and_complete_layout_without_dispatcher_errors()
    {
        RunOnStaThread(() =>
        {
            var main = CreateMain();
            var captures = new CapturesView
            {
                DataContext = new CapturesViewModel(
                [new Screenshot { DisplayTitle = "Ejemplo", UserContext = "Contexto" }])
            };
            var session = new SessionView { DataContext = main.Sessions };
            var capturePanel = new CapturePanelWindow
            {
                DataContext = new CapturePanelViewModel(main)
            };

            Layout(captures);
            Layout(session);
            Layout((FrameworkElement)capturePanel.Content);

            captures.IsInitialized.ShouldBeTrue();
            session.IsInitialized.ShouldBeTrue();
            ((FrameworkElement)capturePanel.Content).IsInitialized.ShouldBeTrue();
            capturePanel.Close();
        });
    }

    [Fact]
    public void Capture_review_tabs_can_all_be_activated_without_writing_to_read_only_preview()
    {
        RunOnStaThread(() =>
        {
            var viewModel = new CapturesViewModel(
                [new Screenshot { DisplayTitle = "Captura", UserContext = "Texto" }]);
            var view = new CapturesView { DataContext = viewModel };
            Layout(view);
            var tabs = FindVisualChild<TabControl>(view).ShouldNotBeNull();

            foreach (var index in Enumerable.Range(0, tabs.Items.Count))
            {
                tabs.SelectedIndex = index;
                Layout(view);
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            }

            tabs.Items.Count.ShouldBe(3);
            viewModel.PreviewMarkdown.ShouldNotBeNull();
        });
    }

    private static MainViewModel CreateMain()
    {
        var sessions = Substitute.For<ISessionRepository>();
        sessions.ListAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<NoteSession>>([]));
        return new MainViewModel(new SessionCoordinator(
            sessions,
            Substitute.For<IScreenshotRepository>(),
            Substitute.For<ISettingsRepository>(),
            Substitute.For<IUnitOfWork>()), sessions);
    }

    private static void Layout(FrameworkElement element)
    {
        element.Measure(new Size(1200, 900));
        element.Arrange(new Rect(0, 0, 1200, 900));
        element.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);
        element.UpdateLayout();
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            if (FindVisualChild<T>(child) is { } nested) return nested;
        }
        return null;
    }

    private static void RunOnStaThread(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { test(); }
            catch (Exception exception) { failure = exception; }
            finally { Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(15)))
            throw new TimeoutException("La prueba WPF no terminó dentro del tiempo previsto.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
