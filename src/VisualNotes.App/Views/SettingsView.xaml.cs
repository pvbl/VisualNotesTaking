namespace VisualNotes.App.Views;

public partial class SettingsView : System.Windows.Controls.UserControl
{
    public SettingsView() => InitializeComponent();

    private void CredentialPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.PasswordBox { DataContext: ViewModels.ApiCredentialEditor editor } passwordBox)
            editor.PendingValue = passwordBox.Password;
    }
}
