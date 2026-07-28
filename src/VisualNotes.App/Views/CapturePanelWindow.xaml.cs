using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

using VisualNotes.App.ViewModels;

namespace VisualNotes.App.Views;

public partial class CapturePanelWindow : Window
{
    private const uint WdaExcludeFromCapture = 0x11;
    private CapturePanelViewModel? _viewModel;
    private Rect _floatingBounds = new(100, 100, 430, 190);
    private bool _isApplyingPlacement;

    public CapturePanelWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        DataContextChanged += OnDataContextChanged;
        LocationChanged += (_, _) => RememberFloatingBounds();
        SizeChanged += (_, _) => RememberFloatingBounds();
        Closed += (_, _) => SubscribeToViewModel(null);
    }

    private void OnSourceInitialized(object? sender, EventArgs args)
    {
        _ = SetWindowDisplayAffinity(new WindowInteropHelper(this).Handle, WdaExcludeFromCapture);
        ApplyPlacement();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs args) =>
        SubscribeToViewModel(args.NewValue as CapturePanelViewModel);

    private void SubscribeToViewModel(CapturePanelViewModel? viewModel)
    {
        if (_viewModel is not null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = viewModel;
        if (_viewModel is not null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        if (new WindowInteropHelper(this).Handle != nint.Zero) ApplyPlacement();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(CapturePanelViewModel.Placement)) ApplyPlacement();
    }

    private void RememberFloatingBounds()
    {
        if (!_isApplyingPlacement && _viewModel?.Placement == CapturePanelPlacement.Flotante
            && WindowState == WindowState.Normal)
            _floatingBounds = new(Left, Top, ActualWidth, ActualHeight);
    }

    private void ApplyPlacement()
    {
        if (_viewModel is null) return;

        var workArea = GetCurrentWorkArea();
        var requested = _viewModel.Placement == CapturePanelPlacement.Flotante
            ? _floatingBounds
            : CapturePanelViewModel.GetPlacementBounds(_viewModel.Placement, workArea, _floatingBounds);
        var bounds = CapturePanelViewModel.ConstrainToWorkArea(requested, workArea);

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

    private Rect GetCurrentWorkArea()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var area = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
        if (PresentationSource.FromVisual(this)?.CompositionTarget is not { } target)
            return new(area.Left, area.Top, area.Width, area.Height);

        var topLeft = target.TransformFromDevice.Transform(new System.Windows.Point(area.Left, area.Top));
        var bottomRight = target.TransformFromDevice.Transform(new System.Windows.Point(area.Right, area.Bottom));
        return new(topLeft, bottomRight);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
}
