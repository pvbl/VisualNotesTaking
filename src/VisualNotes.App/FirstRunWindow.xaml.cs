using System.Drawing;
using System.Text.Json;
using System.Windows;

using Forms = System.Windows.Forms;

namespace VisualNotes.App;

public sealed record BootstrapSettings(string DataDirectory, string Provider, bool AllowRemoteAnalysis, bool EnableHotkeys, bool FirstRunCompleted);

public partial class FirstRunWindow : Window
{
    public FirstRunWindow(string defaultDataDirectory)
    {
        InitializeComponent();
        DataDirectory.Text = defaultDataDirectory;
    }

    public BootstrapSettings? Settings { get; private set; }

    private void BrowseClicked(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog { InitialDirectory = DataDirectory.Text };
        if (dialog.ShowDialog() == Forms.DialogResult.OK) DataDirectory.Text = dialog.SelectedPath;
    }

    private void TestCaptureClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            using var bitmap = new Bitmap(1, 1);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(0, 0, 0, 0, bitmap.Size);
            CaptureResult.Text = "Captura correcta ✓";
        }
        catch (Exception exception)
        {
            CaptureResult.Text = $"No disponible: {exception.Message}";
        }
    }

    private void FinishClicked(object sender, RoutedEventArgs e)
    {
        var directory = DataDirectory.Text.Trim();
        if (string.IsNullOrWhiteSpace(directory))
        {
            MessageBox.Show("Selecciona una carpeta de almacenamiento.", "VisualNotes", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Directory.CreateDirectory(directory);
        var provider = (Provider.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "none";
        Settings = new(directory, provider, AllowRemoteAnalysis.IsChecked == true, EnableHotkeys.IsChecked == true, true);
        DialogResult = true;
    }

    public static BootstrapSettings? Load(string path)
    {
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<BootstrapSettings>(File.ReadAllText(path));
    }

    public static void Save(string path, BootstrapSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, true);
    }
}
