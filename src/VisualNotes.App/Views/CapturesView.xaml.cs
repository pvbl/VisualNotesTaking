using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using VisualNotes.App.ViewModels;
using VisualNotes.Core.Models;

namespace VisualNotes.App.Views;

public partial class CapturesView : UserControl
{
    private Point _dragStart;
    public CapturesView() => InitializeComponent();

    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is CapturesViewModel viewModel)
            viewModel.ReplaceSelection(CaptureList.SelectedItems.Cast<Screenshot>());
    }

    private void CaptureMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && Math.Abs(e.GetPosition(this).Y - _dragStart.Y) > SystemParameters.MinimumVerticalDragDistance)
            DragDrop.DoDragDrop(CaptureList, CaptureList.SelectedItems.Cast<Screenshot>().Select(item => item.Id).ToArray(), DragDropEffects.Move);
        else if (e.LeftButton == MouseButtonState.Pressed) _dragStart = e.GetPosition(this);
    }

    private void CaptureDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not CapturesViewModel viewModel || e.Data.GetData(typeof(Guid[])) is not Guid[]) return;
        var target = (e.OriginalSource as FrameworkElement)?.DataContext as Screenshot;
        viewModel.ReorderCommand.Execute(target is null ? CaptureList.Items.Count : CaptureList.Items.IndexOf(target));
    }
}
