using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;
using VisualNotes.App.ViewModels;
using VisualNotes.Infrastructure;

namespace VisualNotes.App;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _trayIcon;
    private MainWindow? _window;
    private MainViewModel? _viewModel;
    private bool _isExiting;
    private VisualNotesRuntime? _runtime;

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
        _window = new MainWindow { DataContext = _viewModel };
        _window.Closing += (_, args) =>
        {
            if (_isExiting) return;
            args.Cancel = true;
            _window.Hide();
        };
        CreateTrayIcon();
        _window.Show();
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir VisualNotes", null, (_, _) => ShowWindow());
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
        _window?.Close();
        if (_runtime is not null) await _runtime.DisposeAsync();
        Shutdown();
    }
}
