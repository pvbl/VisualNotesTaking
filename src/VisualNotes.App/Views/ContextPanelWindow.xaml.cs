using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

using VisualNotes.App.ViewModels;

namespace VisualNotes.App.Views;

public partial class ContextPanelWindow : Window
{
    private const uint WdaExcludeFromCapture = 0x11;
    private CapturePanelViewModel? _viewModel;
    private CapturePanelWindow? _anchor;
    private Rect _floatingBounds = new(140, 140, 380, 225);
    private bool _isApplyingPlacement;

    public ContextPanelWindow()
    {
        DockInsideCommand = new RelayCommand(_ =>
        {
            if (_viewModel is not null) _viewModel.ContextPlacement = ContextEditorPlacement.Dentro;
        });
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            _ = SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, WdaExcludeFromCapture);
            ApplyPlacement();
        };
        DataContextChanged += (_, args) => SubscribeToViewModel(args.NewValue as CapturePanelViewModel);
        LocationChanged += (_, _) => RememberFloatingBounds();
        SizeChanged += (_, _) => RememberFloatingBounds();
        Closed += (_, _) =>
        {
            SubscribeToViewModel(null);
            Anchor = null;
        };
    }

    public ICommand DockInsideCommand { get; }

    public CapturePanelWindow? Anchor
    {
        get => _anchor;
        set
        {
            if (_anchor is not null)
            {
                _anchor.LocationChanged -= AnchorChanged;
                _anchor.SizeChanged -= AnchorChanged;
            }
            _anchor = value;
            if (_anchor is not null)
            {
                _anchor.LocationChanged += AnchorChanged;
                _anchor.SizeChanged += AnchorChanged;
            }
            ApplyPlacement();
        }
    }

    public void ApplyPlacement()
    {
        if (_viewModel is null || Anchor is null ||
            _viewModel.ContextPlacement == ContextEditorPlacement.Dentro) return;
        if (_viewModel.ContextPlacement == ContextEditorPlacement.Flotante)
        {
            ApplyBounds(_floatingBounds);
            return;
        }

        var anchorBounds = new Rect(Anchor.Left, Anchor.Top, Anchor.ActualWidth, Anchor.ActualHeight);
        var size = new System.Windows.Size(
            double.IsNaN(Width) ? 380 : Width,
            double.IsNaN(Height) ? 225 : Height);
        var requested = CapturePanelViewModel.GetContextBounds(
            _viewModel.ContextPlacement, anchorBounds, size, _floatingBounds);
        ApplyBounds(CapturePanelViewModel.ConstrainToWorkArea(requested, GetAnchorWorkArea()));
    }

    private void SubscribeToViewModel(CapturePanelViewModel? viewModel)
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= ViewModelChanged;
        _viewModel = viewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += ViewModelChanged;
        ApplyPlacement();
    }

    private void ViewModelChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(CapturePanelViewModel.ContextPlacement)) ApplyPlacement();
    }

    private void AnchorChanged(object? sender, EventArgs args) => ApplyPlacement();

    private void RememberFloatingBounds()
    {
        if (!_isApplyingPlacement &&
            _viewModel?.ContextPlacement == ContextEditorPlacement.Flotante &&
            WindowState == WindowState.Normal)
            _floatingBounds = new(Left, Top, ActualWidth, ActualHeight);
    }

    private void ApplyBounds(Rect bounds)
    {
        _isApplyingPlacement = true;
        try
        {
            Left = bounds.Left;
            Top = bounds.Top;
            Width = bounds.Width;
            Height = bounds.Height;
        }
        finally
        {
            _isApplyingPlacement = false;
        }
    }

    private Rect GetAnchorWorkArea()
    {
        var handle = new WindowInteropHelper(Anchor!).Handle;
        var area = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        if (PresentationSource.FromVisual(Anchor)?.CompositionTarget is not { } target)
            return new(area.Left, area.Top, area.Width, area.Height);
        var topLeft = target.TransformFromDevice.Transform(new System.Windows.Point(area.Left, area.Top));
        var bottomRight = target.TransformFromDevice.Transform(new System.Windows.Point(area.Right, area.Bottom));
        return new(topLeft, bottomRight);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
}
