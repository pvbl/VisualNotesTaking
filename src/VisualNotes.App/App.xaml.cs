using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;
using VisualNotes.App.ViewModels;
using VisualNotes.Infrastructure;
using VisualNotes.App.Services;
using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

namespace VisualNotes.App;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _trayIcon;
    private MainWindow? _window;
    private MainViewModel? _viewModel;
    private bool _isExiting;
    private VisualNotesRuntime? _runtime;
    private readonly JsonSettingsStore _regionSettings = new();
    private PersistentRegionService? _regions;
    private WindowsScreenCaptureService? _capture;
    private PersistentRegionBorder? _regionBorder;
    private PersistentCaptureRegion? _activeRegion;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VisualNotes");
        try
        {
            _runtime = await VisualNotesRuntime.CreateAsync(dataDirectory, CancellationToken.None);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"No se pudo preparar la base de datos local.\n\n{exception.Message}", "VisualNotes", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
            return;
        }
        _viewModel = new MainViewModel(_runtime.Coordinator, _runtime.Sessions);
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
        _window.Closing += (_, args) =>
        {
            if (_isExiting) return;
            args.Cancel = true;
            _window.Hide();
        };
        CreateTrayIcon();
        _window.Show();
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
        await _capture.CaptureAsync(new(ScreenCaptureMode.OneTimeRegion, _activeRegion.Bounds,
            MonitorDeviceName: _activeRegion.MonitorDeviceName));
        _regionBorder?.Show(_activeRegion);
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
            pauseItem.Text = _viewModel.IsPaused ? "Reanudar sesión" : "Pausar sesión";
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
        _window?.Close();
        if (_runtime is not null) await _runtime.DisposeAsync();
        Shutdown();
    }
}
