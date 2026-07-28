using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;
using VisualNotes.App.ViewModels;
using VisualNotes.Infrastructure;
using VisualNotes.App.Services;
using VisualNotes.Core.Models;
using VisualNotes.Core.Services;
using Microsoft.Extensions.Logging;
using VisualNotes.Infrastructure.Diagnostics;

namespace VisualNotes.App;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _trayIcon;
    private MainWindow? _window;
    private Views.CapturePanelWindow? _capturePanel;
    private CapturePanelViewModel? _capturePanelViewModel;
    private MainViewModel? _viewModel;
    private bool _isExiting;
    private VisualNotesRuntime? _runtime;
    private readonly JsonSettingsStore _regionSettings = new();
    private PersistentRegionService? _regions;
    private WindowsScreenCaptureService? _capture;
    private PersistentRegionBorder? _regionBorder;
    private PersistentCaptureRegion? _activeRegion;
    private IGlobalHotkeyService? _hotkeys;
    private ILoggerFactory? _loggerFactory;
    private Serilog.ILogger? _serilog;
    private GlobalExceptionHandler? _exceptions;
    private VisualNotesTelemetry? _telemetry;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VisualNotes");
        (_loggerFactory, _serilog) = LoggingFactory.Create(Path.Combine(dataDirectory, "diagnostics", "visualnotes-.json"));
        _telemetry = new VisualNotesTelemetry(enableLocalConsoleExporter: false);
        _exceptions = new GlobalExceptionHandler(_loggerFactory.CreateLogger<GlobalExceptionHandler>(), ShowError, code => Shutdown(code));
        DispatcherUnhandledException += (_, args) => { _exceptions.HandleDispatcher(args.Exception); args.Handled = true; };
        TaskScheduler.UnobservedTaskException += (_, args) => { _exceptions.HandleUnobservedTask(args.Exception); args.SetObserved(); };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => _exceptions.HandleCritical(
            args.ExceptionObject as Exception ?? new InvalidOperationException("Fallo crítico no administrado."));
        try
        {
            _runtime = await VisualNotesRuntime.CreateAsync(dataDirectory, CancellationToken.None);
        }
        catch (Exception exception)
        {
            _loggerFactory.CreateLogger<App>().LogCritical(exception, "Local database initialization failed");
            _exceptions.HandleDispatcher(exception);
            Shutdown(-1);
            return;
        }
        _hotkeys = new GlobalHotkeyService(new WindowsGlobalHotkeyAdapter());
        _viewModel = new MainViewModel(_runtime.Coordinator, _runtime.Sessions, _hotkeys, _runtime.ApiCredentials);
        _hotkeys.HotkeyInvoked += OnHotkeyInvoked;
        var hotkeyResult = _hotkeys.Apply(SettingsViewModel.DefaultBindings());
        if (!hotkeyResult.Succeeded)
            MessageBox.Show(string.Join(Environment.NewLine, hotkeyResult.Conflicts.Select(x => x.Message)),
                "Conflicto de atajos", MessageBoxButton.OK, MessageBoxImage.Warning);
        await _viewModel.InitializeAsync();
        _regions = new PersistentRegionService(_regionSettings);
        _capture = new WindowsScreenCaptureService(new RegionSelectionOverlay());
        _regionBorder = new PersistentRegionBorder();
        _viewModel.CaptureRegionRequested += CapturePersistentRegion;
        _viewModel.RedefineRegionRequested += RedefinePersistentRegion;
        var restored = await _regions.RestoreAsync(WindowsScreenCaptureService.GetMonitors(),
            _viewModel.ActiveSession?.Id ?? Guid.Empty);
        _activeRegion = restored.Region;
        _window = new MainWindow { DataContext = _viewModel };
        _capturePanelViewModel = new CapturePanelViewModel(_viewModel);
        _capturePanelViewModel.CaptureRequested += CaptureFromPanel;
        _capturePanel = new Views.CapturePanelWindow { DataContext = _capturePanelViewModel, Owner = _window };
        _window.Closing += (_, args) =>
        {
            if (_isExiting) return;
            args.Cancel = true;
            _window.Hide();
        };
        CreateTrayIcon();
        _window.Show();
        _capturePanel.Show();
        if (_activeRegion is not null) _regionBorder.Show(_activeRegion);
    }

    private async void RedefinePersistentRegion()
    {
        if (_capture is null || _regions is null || _viewModel is null) return;
        var frame = await _capture.CaptureAsync(new(ScreenCaptureMode.OneTimeRegion));
        if (frame?.Metadata is not { } metadata) return;
        var monitors = WindowsScreenCaptureService.GetMonitors();
        var monitor = monitors.FirstOrDefault(x => x.DeviceName == metadata.MonitorDeviceName) ?? monitors.First();
        _activeRegion = new(metadata.PhysicalBounds, monitor.DeviceName, monitor.DpiX, monitor.DpiY,
            _viewModel.ActiveSession?.Id ?? Guid.Empty, monitor.Bounds);
        await _regions.SaveAsync(_activeRegion);
        _regionBorder?.Show(_activeRegion);
    }

    private async void CapturePersistentRegion()
    {
        if (_capture is null || _activeRegion is null || _activeRegion.IsHidden) return;
        var frame = await _capture.CaptureAsync(new(ScreenCaptureMode.OneTimeRegion, _activeRegion.Bounds,
            MonitorDeviceName: _activeRegion.MonitorDeviceName));
        if (frame is not null) _capturePanelViewModel?.CaptureCompleted();
        _regionBorder?.Show(_activeRegion);
    }

    private async void CaptureFromPanel(CapturePanelMode mode)
    {
        if (_capture is null) return;
        if (mode == CapturePanelMode.Region && _activeRegion is not null) { CapturePersistentRegion(); return; }
        var captureMode = mode switch { CapturePanelMode.Monitor => ScreenCaptureMode.CurrentMonitor, CapturePanelMode.Desktop => ScreenCaptureMode.FullVirtualDesktop, CapturePanelMode.Window => ScreenCaptureMode.ActiveWindow, _ => ScreenCaptureMode.OneTimeRegion };
        var frame = await _capture.CaptureAsync(new(captureMode));
        if (frame is not null) _capturePanelViewModel?.CaptureCompleted();
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir VisualNotes", null, (_, _) => ShowWindow());
        menu.Items.Add("Capturar región (Ctrl+Mayús+C)", null, (_, _) => CapturePersistentRegion());
        menu.Items.Add("Redefinir región (Ctrl+Mayús+R)", null, (_, _) => RedefinePersistentRegion());
        var pauseItem = menu.Items.Add("Pausar sesión");
        pauseItem.Click += (_, _) =>
        {
            _viewModel!.TogglePauseCommand.Execute(null);
            pauseItem.Text = _viewModel.ActiveSession?.IsPaused == true ? "Reanudar sesión" : "Pausar sesión";
        };
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Salir", null, (_, _) => ExitApplication());

        _trayIcon = new Forms.NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "VisualNotes",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => ShowWindow();
    }

    private void ShowWindow()
    {
        _window!.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private async void ExitApplication()
    {
        _isExiting = true;
        _trayIcon?.Dispose();
        _regionBorder?.Dispose();
        _capturePanel?.Close();
        _hotkeys?.Dispose();
        _window?.Close();
        if (_runtime is not null) await _runtime.DisposeAsync();
        _telemetry?.Dispose();
        _loggerFactory?.Dispose();
        (_serilog as IDisposable)?.Dispose();
        Shutdown();
    }

    private static void ShowError(UserFacingError error) => MessageBox.Show(error.Message, error.Title, MessageBoxButton.OK, MessageBoxImage.Error);

    private async void OnHotkeyInvoked(object? sender, HotkeyAction action)
    {
        if (_viewModel is null || _capture is null) return;
        switch (action)
        {
            case HotkeyAction.CaptureRegion: CapturePersistentRegion(); break;
            case HotkeyAction.CaptureFullDesktop: await _capture.CaptureAsync(new(ScreenCaptureMode.FullVirtualDesktop)); break;
            case HotkeyAction.CaptureCurrentMonitor: await _capture.CaptureAsync(new(ScreenCaptureMode.CurrentMonitor)); break;
            case HotkeyAction.CaptureActiveWindow: await _capture.CaptureAsync(new(ScreenCaptureMode.ActiveWindow)); break;
            case HotkeyAction.TogglePause: _viewModel.TogglePauseCommand.Execute(null); break;
            case HotkeyAction.NextSection: ChangeSection(1); break;
            case HotkeyAction.PreviousSection: ChangeSection(-1); break;
            case HotkeyAction.Undo: _viewModel.UndoRequested?.Invoke(); break;
            case HotkeyAction.MarkImportant: _viewModel.MarkImportantRequested?.Invoke(); break;
            case HotkeyAction.AddContext: _viewModel.AddContextRequested?.Invoke(); break;
        }
    }

    private void ChangeSection(int offset)
    {
        if (_viewModel?.ActiveSession is not { } session) return;
        var sections = session.Sections.OrderBy(section => section.Order).ToArray();
        if (sections.Length == 0) return;
        var current = Array.FindIndex(sections, section => section.Id == session.ActiveSectionId);
        var next = sections[Math.Clamp(current + offset, 0, sections.Length - 1)];
        _viewModel.Sessions.ActivateSectionCommand.Execute(next);
    }
}
