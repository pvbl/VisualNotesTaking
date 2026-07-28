using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;

using Microsoft.Extensions.Logging;

using VisualNotes.App.Services;
using VisualNotes.App.ViewModels;
using VisualNotes.Core.Models;
using VisualNotes.Core.Services;
using VisualNotes.Infrastructure;
using VisualNotes.Infrastructure.Diagnostics;

using Forms = System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace VisualNotes.App;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = @"Local\VisualNotes.App.SingleInstance";

    private static class NativeWindow
    {
        private const int ShowNormal = 1;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern nint FindWindow(string? className, string windowName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(nint window);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindow(nint window, int command);

        public static void ActivateRunningInstance()
        {
            var window = FindWindow(null, "VisualNotes");
            if (window == 0)
            {
                return;
            }

            ShowWindow(window, ShowNormal);
            SetForegroundWindow(window);
        }
    }

    private sealed class MessageBoxNotificationService : INotificationService
    {
        public void ShowError(string message) => MessageBox.Show(message, "Operación no completada", MessageBoxButton.OK, MessageBoxImage.Error);
    }
    private Icon? _applicationIcon;
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
    private Mutex? _singleInstanceMutex;

    protected override async void OnStartup(StartupEventArgs e) => await StartAsync(e);

    private async Task StartAsync(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstanceMutex = new Mutex(true, SingleInstanceMutexName, out var isPrimaryInstance);
        if (!isPrimaryInstance)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            NativeWindow.ActivateRunningInstance();
            Shutdown();
            return;
        }

        AsyncRelayCommand.DefaultNotificationService = new MessageBoxNotificationService();
        if (e.Args is ["--smoke-test", var markerPath])
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(markerPath))!);
            await File.WriteAllTextAsync(markerPath, $"VisualNotes {RuntimeInformation.ProcessArchitecture} OK");
            Shutdown();
            return;
        }
        var localDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VisualNotes");
        var bootstrapPath = Path.Combine(localDirectory, "bootstrap.json");
        var environmentDirectory = Environment.GetEnvironmentVariable("VISUALNOTES_DATA_DIRECTORY");
        var bootstrap = FirstRunWindow.Load(bootstrapPath);
        if (bootstrap?.FirstRunCompleted != true && environmentDirectory is null)
        {
            var firstRun = new FirstRunWindow(localDirectory);
            if (firstRun.ShowDialog() != true || firstRun.Settings is null) { Shutdown(); return; }
            bootstrap = firstRun.Settings;
            FirstRunWindow.Save(bootstrapPath, bootstrap);
        }
        var dataDirectory = environmentDirectory ?? bootstrap?.DataDirectory ?? localDirectory;
        (_loggerFactory, _serilog) = LoggingFactory.Create(Path.Combine(dataDirectory, "diagnostics", "visualnotes-.json"));
        _telemetry = new VisualNotesTelemetry(enableLocalConsoleExporter: false);
        _exceptions = new GlobalExceptionHandler(_loggerFactory!.CreateLogger<GlobalExceptionHandler>(), ShowError, code => Shutdown(code));
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
        MainViewModel? composedViewModel = null;
        var captureActions = new CaptureActionCoordinator(_runtime.CaptureWorkspace,
            () => composedViewModel?.ActiveSession,
            capture =>
            {
                if (composedViewModel is null) return;
                composedViewModel.Captures.SelectedCapture = capture;
                composedViewModel.NavigateCommand.Execute("Captures");
                ShowWindow();
            },
            message => MessageBox.Show(message, "Acción de captura", MessageBoxButton.OK, MessageBoxImage.Information));
        _viewModel = composedViewModel = new MainViewModel(_runtime.Coordinator, _runtime.Sessions, _hotkeys, _runtime.ApiCredentials,
            _runtime.CaptureWorkspace, _runtime.DocumentExporter, new WindowsExportInteraction(),
            _runtime.Settings, _runtime.UnitOfWork, _runtime.AnalysisJobs, captureActions);
        _ = _runtime.RecoverIncompleteJobsAsync();
        _hotkeys.HotkeyInvoked += OnHotkeyInvoked;
        var bindings = bootstrap?.EnableHotkeys == false
            ? SettingsViewModel.DefaultBindings().Select(binding => binding with { IsEnabled = false }).ToArray()
            : SettingsViewModel.DefaultBindings();
        var hotkeyResult = _hotkeys.Apply(bindings);
        if (!hotkeyResult.Succeeded)
            MessageBox.Show(string.Join(Environment.NewLine, hotkeyResult.Conflicts.Select(x => x.Message)),
                "Conflicto de atajos", MessageBoxButton.OK, MessageBoxImage.Warning);
        await _viewModel.InitializeAsync();
        _regions = new PersistentRegionService(_regionSettings);
        _capture = new WindowsScreenCaptureService(new RegionSelectionOverlay());
        _regionBorder = new PersistentRegionBorder();
        _viewModel.CaptureRegionRequested += CapturePersistentRegionAsync;
        _viewModel.RedefineRegionRequested += RedefinePersistentRegionAsync;
        var restored = await _regions.RestoreAsync(WindowsScreenCaptureService.GetMonitors(),
            _viewModel.ActiveSession?.Id ?? Guid.Empty);
        _activeRegion = restored.Region;
        _window = new MainWindow { DataContext = _viewModel };
        _capturePanelViewModel = new CapturePanelViewModel(_viewModel);
        _capturePanelViewModel.CaptureRequested += CaptureFromPanel;
        _capturePanel = new Views.CapturePanelWindow { DataContext = _capturePanelViewModel };
        _capturePanel.Closing += (_, args) =>
        {
            if (_isExiting) return;
            args.Cancel = true;
            _capturePanel.Hide();
        };
        _viewModel.SessionActivated += ShowCapturePanel;
        _window.Closing += (_, args) =>
        {
            if (_isExiting) return;
            args.Cancel = true;
            _window.Hide();
        };
        CreateTrayIcon();
        _window.Show();
        // Keep capture controls independent so minimizing or hiding the main window
        // does not remove them from the screen during a capture session.
        _capturePanel.Show();
        if (_activeRegion is not null) _regionBorder.Show(_activeRegion);
    }

    private async void RedefinePersistentRegion() => await RedefinePersistentRegionAsync();
    private async Task RedefinePersistentRegionAsync()
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

    private async void CapturePersistentRegion() => await CapturePersistentRegionAsync();
    private Task CapturePersistentRegionAsync() => CapturePersistentRegionAsync(string.Empty, false);
    private async Task CapturePersistentRegionAsync(string contextMarkdown, bool clearContext)
    {
        if (_capture is null || _activeRegion is null || _activeRegion.IsHidden || _viewModel is null) return;
        await _viewModel.EnsureActiveSessionAsync();
        await _viewModel.ResumeActiveSessionAsync();
        var frame = await _capture.CaptureAsync(new(ScreenCaptureMode.OneTimeRegion, _activeRegion.Bounds,
            MonitorDeviceName: _activeRegion.MonitorDeviceName));
        if (frame is not null) await PersistCapturedFrameAsync(frame, contextMarkdown, clearContext);
        _regionBorder?.Show(_activeRegion);
    }

    private async void CaptureFromPanel(CapturePanelMode mode, string contextMarkdown) => await CaptureFromPanelAsync(mode, contextMarkdown);
    private async Task CaptureFromPanelAsync(CapturePanelMode mode, string contextMarkdown)
    {
        if (_capture is null || _viewModel is null) return;
        await _viewModel.EnsureActiveSessionAsync();
        await _viewModel.ResumeActiveSessionAsync();
        if (mode == CapturePanelMode.Region && _activeRegion is not null) { await CapturePersistentRegionAsync(contextMarkdown, true); return; }
        var captureMode = mode switch { CapturePanelMode.Monitor => ScreenCaptureMode.CurrentMonitor, CapturePanelMode.Desktop => ScreenCaptureMode.FullVirtualDesktop, CapturePanelMode.Window => ScreenCaptureMode.ActiveWindow, _ => ScreenCaptureMode.OneTimeRegion };
        var frame = await _capture.CaptureAsync(new(captureMode));
        if (frame is not null) await PersistCapturedFrameAsync(frame, contextMarkdown, true);
    }

    private void CreateTrayIcon()
    {
        var executablePath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(executablePath))
            _applicationIcon = Icon.ExtractAssociatedIcon(executablePath);

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
            Icon = _applicationIcon ?? SystemIcons.Application,
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

    private void ShowCapturePanel()
    {
        if (_capturePanel is null || _isExiting) return;
        if (!_capturePanel.IsVisible) _capturePanel.Show();
        if (_capturePanel.WindowState == WindowState.Minimized) _capturePanel.WindowState = WindowState.Normal;
        _capturePanel.Activate();
    }

    private async void ExitApplication() => await ExitApplicationAsync();
    private async Task ExitApplicationAsync()
    {
        _isExiting = true;
        _trayIcon?.Dispose();
        _applicationIcon?.Dispose();
        _regionBorder?.Dispose();
        _capturePanel?.Close();
        _hotkeys?.Dispose();
        _window?.Close();
        await AsyncCommandOperations.CancelAndWaitAsync();
        if (_runtime is not null) await _runtime.DisposeAsync();
        _telemetry?.Dispose();
        _loggerFactory?.Dispose();
        (_serilog as IDisposable)?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        _singleInstanceMutex = null;
        Shutdown();
    }

    private static void ShowError(UserFacingError error) => MessageBox.Show(error.Message, error.Title, MessageBoxButton.OK, MessageBoxImage.Error);

    private async Task PersistCapturedFrameAsync(CapturedFrame frame, string contextMarkdown = "", bool clearContext = false)
    {
        if (_runtime is null || _viewModel?.ActiveSession is not { } session)
        {
            MessageBox.Show("Crea o continúa una sesión antes de capturar.", "Captura no guardada", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (session.IsPaused)
        {
            MessageBox.Show("Reanuda la sesión antes de capturar.", "Captura no guardada", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (session.ActiveSectionId is null || session.Sections.All(section => section.Id != session.ActiveSectionId))
        {
            MessageBox.Show("Selecciona o crea una sección activa antes de capturar.", "Captura no guardada", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            await using var content = new MemoryStream(frame.ImageData, writable: false);
            var stored = await _runtime.ScreenshotStorage.StoreAsync(new ScreenshotStorageRequest(
                session.Id, frame.Id, content,
                frame.MediaType.Equals("image/png", StringComparison.OrdinalIgnoreCase)
                    ? ScreenshotContentKind.CodeOrSmallText : ScreenshotContentKind.VideoOrImage));
            var metadata = frame.Metadata;
            var capture = new Screenshot
            {
                Id = frame.Id,
                SessionId = session.Id,
                SectionId = session.ActiveSectionId,
                CapturedAt = metadata?.CapturedAt ?? frame.CapturedAt,
                Width = metadata?.PixelWidth ?? frame.Region.Width,
                Height = metadata?.PixelHeight ?? frame.Region.Height,
                PerceptualHash = stored.Optimized.Sha256,
                UserContext = contextMarkdown.Trim(),
                Image = new ScreenshotImage
                {
                    ScreenshotId = frame.Id,
                    RelativePath = stored.Optimized.RelativePath,
                    MediaType = stored.Optimized.Format.Equals("PNG", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg",
                    ByteLength = stored.Optimized.Size,
                    Sha256 = stored.Optimized.Sha256
                },
                Context = new ScreenshotContext
                {
                    ScreenshotId = frame.Id,
                    WindowTitle = metadata?.WindowTitle,
                    MonitorDeviceName = metadata?.MonitorDeviceName,
                    WindowHandle = metadata?.WindowHandle is { } handle ? (long)handle : null,
                    CaptureMode = metadata?.Mode,
                    PhysicalX = metadata?.PhysicalBounds.X ?? frame.Region.X,
                    PhysicalY = metadata?.PhysicalBounds.Y ?? frame.Region.Y,
                    DpiX = metadata?.DpiX ?? 96,
                    DpiY = metadata?.DpiY ?? 96
                }
            };
            await _runtime.Coordinator.AddCaptureAsync(session, capture);
            await _viewModel.CaptureAddedAsync(capture);
            _capturePanelViewModel?.CaptureCompleted(clearContext);
        }
        catch (Exception exception)
        {
            _loggerFactory?.CreateLogger<App>().LogError(exception, "Screenshot persistence failed");
            MessageBox.Show($"No se pudo registrar la captura en la base de datos.{Environment.NewLine}{Environment.NewLine}Detalle: {exception.GetBaseException().Message}",
                "Captura no guardada", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnHotkeyInvoked(object? sender, HotkeyAction action) => await HandleHotkeyAsync(action);
    private async Task HandleHotkeyAsync(HotkeyAction action)
    {
        if (_viewModel is null || _capture is null) return;
        switch (action)
        {
            case HotkeyAction.CaptureRegion: await CapturePersistentRegionAsync(); break;
            case HotkeyAction.CaptureFullDesktop: await CaptureAndPersistAsync(ScreenCaptureMode.FullVirtualDesktop); break;
            case HotkeyAction.CaptureCurrentMonitor: await CaptureAndPersistAsync(ScreenCaptureMode.CurrentMonitor); break;
            case HotkeyAction.CaptureActiveWindow: await CaptureAndPersistAsync(ScreenCaptureMode.ActiveWindow); break;
            case HotkeyAction.TogglePause: _viewModel.TogglePauseCommand.Execute(null); break;
            case HotkeyAction.NextSection: ChangeSection(1); break;
            case HotkeyAction.PreviousSection: ChangeSection(-1); break;
            case HotkeyAction.Undo: ExecuteIfAvailable(_viewModel.UndoCommand); break;
            case HotkeyAction.MarkImportant: ExecuteIfAvailable(_viewModel.MarkImportantCommand); break;
            case HotkeyAction.AddContext: ExecuteIfAvailable(_viewModel.AddContextCommand); break;
        }
    }

    private static void ExecuteIfAvailable(System.Windows.Input.ICommand command)
    {
        if (command.CanExecute(null)) command.Execute(null);
    }

    private async Task CaptureAndPersistAsync(ScreenCaptureMode mode)
    {
        if (_capture is null || _viewModel is null) return;
        await _viewModel.EnsureActiveSessionAsync();
        await _viewModel.ResumeActiveSessionAsync();
        var frame = await _capture.CaptureAsync(new(mode));
        if (frame is not null) await PersistCapturedFrameAsync(frame);
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
