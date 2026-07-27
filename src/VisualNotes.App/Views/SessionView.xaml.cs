namespace VisualNotes.App.Views;

public partial class SessionView : System.Windows.Controls.UserControl
{
    public SessionView() => InitializeComponent();

    private void AutoSaveSection(object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is ViewModels.SessionViewModel viewModel && viewModel.SelectedSection is not null)
            viewModel.RenameSectionCommand.Execute(null);
    }
}
