using System.Windows;
using System.Windows.Input;

using VisualNotes.App.Services;

namespace VisualNotes.App.Views;

using WpfButton = System.Windows.Controls.Button;
using WpfDockPanel = System.Windows.Controls.DockPanel;
using WpfListBox = System.Windows.Controls.ListBox;
using WpfStackPanel = System.Windows.Controls.StackPanel;
using WpfTextBlock = System.Windows.Controls.TextBlock;

/// <summary>Keyboard-accessible chooser that prevents the capture panel from becoming the target window.</summary>
public sealed class WindowCapturePicker : Window
{
    private readonly WpfListBox _windows;

    private WindowCapturePicker(IReadOnlyList<CapturableWindow> windows)
    {
        Title = "Elegir ventana";
        Width = 560;
        Height = 420;
        MinWidth = 380;
        MinHeight = 260;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        ShowInTaskbar = false;

        _windows = new WpfListBox
        {
            ItemsSource = windows,
            DisplayMemberPath = nameof(CapturableWindow.Title),
            Margin = new Thickness(0, 10, 0, 12)
        };
        _windows.MouseDoubleClick += (_, _) => Accept();
        _windows.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Enter) Accept();
        };

        var choose = new WpfButton
        {
            Content = "Capturar esta ventana",
            IsDefault = true,
            Padding = new Thickness(14, 8, 14, 8),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        choose.Click += (_, _) => Accept();
        var cancel = new WpfButton
        {
            Content = "Cancelar",
            IsCancel = true,
            Padding = new Thickness(14, 8, 14, 8),
            Margin = new Thickness(0, 0, 8, 0)
        };

        var actions = new WpfStackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        actions.Children.Add(cancel);
        actions.Children.Add(choose);
        var content = new WpfDockPanel { Margin = new Thickness(20) };
        WpfDockPanel.SetDock(actions, System.Windows.Controls.Dock.Bottom);
        content.Children.Add(actions);
        var heading = new WpfTextBlock
        {
            Text = "Selecciona la ventana que quieres capturar",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold
        };
        WpfDockPanel.SetDock(heading, System.Windows.Controls.Dock.Top);
        content.Children.Add(heading);
        content.Children.Add(_windows);
        Content = content;
        Loaded += (_, _) =>
        {
            _windows.SelectedIndex = windows.Count > 0 ? 0 : -1;
            _windows.Focus();
        };
    }

    public nint? SelectedHandle => (_windows.SelectedItem as CapturableWindow)?.Handle;

    public static nint? Choose(IReadOnlyList<CapturableWindow> windows)
    {
        if (windows.Count == 0)
        {
            System.Windows.MessageBox.Show("No hay otras ventanas visibles para capturar.", "Sin ventanas",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }
        var picker = new WindowCapturePicker(windows);
        return picker.ShowDialog() == true ? picker.SelectedHandle : null;
    }

    private void Accept()
    {
        if (_windows.SelectedItem is null) return;
        DialogResult = true;
    }
}
