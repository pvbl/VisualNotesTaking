using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using VisualNotes.Core.Models;
using VisualNotes.Core.Services;
using Forms = System.Windows.Forms;

namespace VisualNotes.App.Services;

/// <summary>One transparent, DPI-aware selection surface per monitor.</summary>
public sealed class RegionSelectionOverlay : IRegionSelectionOverlay
{
    public Task<PhysicalRectangle?> SelectAsync(CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<PhysicalRectangle?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = new RegionSelectionController();
        var windows = Forms.Screen.AllScreens.Select(screen => new SelectionWindow(screen, controller)).ToList();

        void Close(PhysicalRectangle? result)
        {
            foreach (var window in windows) window.Close();
            completion.TrySetResult(result);
        }
        foreach (var window in windows)
        {
            window.ConfirmRequested += () => { if (controller.Confirm()) Close(controller.Selection); };
            window.CancelRequested += () => { controller.Cancel(); Close(null); };
            window.ResetRequested += () => { controller.Reset(); foreach (var item in windows) item.RefreshSelection(); };
            window.SelectionChanged += () => { foreach (var item in windows) item.RefreshSelection(); };
            window.Show();
        }
        var registration = cancellationToken.Register(() => Application.Current.Dispatcher.Invoke(() => Close(null)));
        return AwaitAndDisposeAsync(completion.Task, registration);
    }

    private static async Task<PhysicalRectangle?> AwaitAndDisposeAsync(Task<PhysicalRectangle?> task, CancellationTokenRegistration registration)
    {
        try { return await task.ConfigureAwait(false); }
        finally { registration.Dispose(); }
    }

    private sealed class SelectionWindow : Window
    {
        private readonly Forms.Screen _screen;
        private readonly RegionSelectionController _controller;
        private readonly Canvas _canvas = new();
        private readonly Rectangle _selection = new() { Stroke = Brushes.DeepSkyBlue, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(35, 0, 160, 255)) };
        public event Action? ConfirmRequested;
        public event Action? CancelRequested;
        public event Action? ResetRequested;
        public event Action? SelectionChanged;

        internal SelectionWindow(Forms.Screen screen, RegionSelectionController controller)
        {
            _screen = screen; _controller = controller;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = new SolidColorBrush(Color.FromArgb(55, 0, 0, 0));
            Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize;
            var source = PresentationSource.FromVisual(Application.Current.MainWindow);
            var scaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1;
            var scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1;
            Left = screen.Bounds.Left / scaleX; Top = screen.Bounds.Top / scaleY;
            Width = screen.Bounds.Width / scaleX; Height = screen.Bounds.Height / scaleY;
            Content = BuildContent();
            PreviewMouseLeftButtonDown += OnMouseDown;
            PreviewMouseMove += OnMouseMove;
            PreviewMouseLeftButtonUp += (_, _) => ReleaseMouseCapture();
            PreviewKeyDown += OnKeyDown;
        }

        private UIElement BuildContent()
        {
            var root = new Grid(); root.Children.Add(_canvas); _canvas.Children.Add(_selection);
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Background = Brushes.White, Margin = new Thickness(12), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
            panel.Children.Add(Button("Confirmar", "ConfirmSelection", () => ConfirmRequested?.Invoke()));
            panel.Children.Add(Button("Bloquear", "LockSelection", () => { _controller.SetLocked(!_controller.IsLocked); SelectionChanged?.Invoke(); }));
            panel.Children.Add(Button("Ocultar", "HideSelection", () => { _controller.SetHidden(!_controller.IsHidden); SelectionChanged?.Invoke(); }));
            panel.Children.Add(Button("Reiniciar", "ResetSelection", () => ResetRequested?.Invoke()));
            panel.Children.Add(Button("Cancelar", "CancelSelection", () => CancelRequested?.Invoke()));
            root.Children.Add(panel); return root;
        }

        private static Button Button(string text, string automationId, Action action)
        {
            var button = new Button { Content = text, Margin = new Thickness(4), Padding = new Thickness(10, 4, 10, 4) };
            System.Windows.Automation.AutomationProperties.SetAutomationId(button, automationId);
            button.Click += (_, _) => action(); return button;
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs args)
        {
            var point = PointToScreen(args.GetPosition(this));
            _controller.Begin((int)Math.Round(point.X), (int)Math.Round(point.Y));
            CaptureMouse(); SelectionChanged?.Invoke();
        }

        private void OnMouseMove(object sender, MouseEventArgs args)
        {
            if (args.LeftButton != MouseButtonState.Pressed) return;
            var point = PointToScreen(args.GetPosition(this));
            _controller.Update((int)Math.Round(point.X), (int)Math.Round(point.Y)); SelectionChanged?.Invoke();
        }

        private void OnKeyDown(object sender, KeyEventArgs args)
        {
            if (args.Key == Key.Escape) CancelRequested?.Invoke();
            else if (args.Key == Key.Enter) ConfirmRequested?.Invoke();
            else if (args.Key == Key.R) ResetRequested?.Invoke();
            else if (args.Key == Key.L) { _controller.SetLocked(!_controller.IsLocked); SelectionChanged?.Invoke(); }
            else if (args.Key == Key.H) { _controller.SetHidden(!_controller.IsHidden); SelectionChanged?.Invoke(); }
            else if (args.Key is Key.Left or Key.Right or Key.Up or Key.Down)
            {
                var x = args.Key == Key.Left ? -1 : args.Key == Key.Right ? 1 : 0;
                var y = args.Key == Key.Up ? -1 : args.Key == Key.Down ? 1 : 0;
                if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) _controller.ResizeBy(x, y);
                else _controller.MoveBy(x, y);
                SelectionChanged?.Invoke(); args.Handled = true;
            }
        }

        internal void RefreshSelection()
        {
            if (_controller.IsHidden || _controller.Selection is not { } region) { _selection.Visibility = Visibility.Collapsed; return; }
            var intersection = ScreenCaptureGeometry.Intersect(region,
                new(_screen.Bounds.X, _screen.Bounds.Y, _screen.Bounds.Width, _screen.Bounds.Height));
            if (intersection.IsEmpty) { _selection.Visibility = Visibility.Collapsed; return; }
            var topLeft = PointFromScreen(new Point(intersection.X, intersection.Y));
            var bottomRight = PointFromScreen(new Point(intersection.Right, intersection.Bottom));
            Canvas.SetLeft(_selection, topLeft.X); Canvas.SetTop(_selection, topLeft.Y);
            _selection.Width = bottomRight.X - topLeft.X; _selection.Height = bottomRight.Y - topLeft.Y;
            _selection.Visibility = Visibility.Visible;
        }
    }
}
