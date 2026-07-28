using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfPoint = System.Windows.Point;
using WpfRectangle = System.Windows.Shapes.Rectangle;

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
        var registration = cancellationToken.Register(() => WpfApplication.Current.Dispatcher.Invoke(() => Close(null)));
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
        // A null background makes empty Canvas areas transparent to WPF hit testing.
        // Transparent keeps the overlay visually unchanged while allowing a drag to
        // start anywhere outside the command panel.
        private readonly Canvas _canvas = new() { Background = WpfBrushes.Transparent };
        private readonly TextBlock _status = new();
        private readonly WpfRectangle _selection = new() { Stroke = WpfBrushes.DeepSkyBlue, StrokeThickness = 2, Fill = new SolidColorBrush(WpfColor.FromArgb(35, 0, 160, 255)) };
        private bool _isDragging;
        public event Action? ConfirmRequested;
        public event Action? CancelRequested;
        public event Action? ResetRequested;
        public event Action? SelectionChanged;

        internal SelectionWindow(Forms.Screen screen, RegionSelectionController controller)
        {
            _screen = screen; _controller = controller;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = new SolidColorBrush(WpfColor.FromArgb(55, 0, 0, 0));
            Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize;
            AutomationProperties.SetAutomationId(this, "RegionSelectionWindow");
            AutomationProperties.SetName(this, "Selector de región de captura");
            AutomationProperties.SetHelpText(this, "Arrastra para seleccionar. Enter confirma, Escape cancela, flechas mueven y Mayús más flechas cambia el tamaño.");
            var source = PresentationSource.FromVisual(WpfApplication.Current.MainWindow);
            var scaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1;
            var scaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1;
            Left = screen.Bounds.Left / scaleX; Top = screen.Bounds.Top / scaleY;
            Width = screen.Bounds.Width / scaleX; Height = screen.Bounds.Height / scaleY;
            Content = BuildContent();
            // Start a selection only from the drawing surface. Listening at window
            // preview level also receives clicks intended for the command buttons;
            // capturing the mouse there prevents Confirm, Cancel, Reset, etc. from
            // completing their Click event and replaces the selection with 0x0.
            _canvas.MouseLeftButtonDown += OnMouseDown;
            PreviewMouseMove += OnMouseMove;
            PreviewMouseLeftButtonUp += (_, _) =>
            {
                _isDragging = false;
                ReleaseMouseCapture();
            };
            PreviewKeyDown += OnKeyDown;
            Loaded += (_, _) => Keyboard.Focus(_canvas);
        }

        private UIElement BuildContent()
        {
            var root = new Grid(); root.Children.Add(_canvas); _canvas.Children.Add(_selection);
            _canvas.Focusable = true;
            var panel = new StackPanel { Background = WpfBrushes.White, Margin = new Thickness(12), HorizontalAlignment = WpfHorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
            var controls = new StackPanel { Orientation = WpfOrientation.Horizontal };
            controls.Children.Add(Button("Confirmar", "ConfirmSelection", "Confirma la región seleccionada; Enter", () => ConfirmRequested?.Invoke()));
            controls.Children.Add(Button("Bloquear", "LockSelection", "Impide mover o redimensionar la región; L", () => { _controller.SetLocked(!_controller.IsLocked); SelectionChanged?.Invoke(); }));
            controls.Children.Add(Button("Ocultar", "HideSelection", "Oculta o muestra el borde de la región; H", () => { _controller.SetHidden(!_controller.IsHidden); SelectionChanged?.Invoke(); }));
            controls.Children.Add(Button("Eliminar", "DeleteSelection", "Elimina la región actual; Suprimir", () => { _controller.Delete(); SelectionChanged?.Invoke(); }));
            controls.Children.Add(Button("Reiniciar", "ResetSelection", "Descarta la región para volver a dibujarla; R", () => ResetRequested?.Invoke()));
            controls.Children.Add(Button("Cancelar", "CancelSelection", "Cierra el selector sin capturar; Escape", () => CancelRequested?.Invoke()));
            panel.Children.Add(controls);
            _status.Foreground = WpfBrushes.Black; _status.Margin = new Thickness(6); _status.Text = "Sin región seleccionada";
            AutomationProperties.SetAutomationId(_status, "RegionSelectionStatus");
            AutomationProperties.SetName(_status, "Estado de selección");
            AutomationProperties.SetLiveSetting(_status, AutomationLiveSetting.Polite);
            panel.Children.Add(_status);
            root.Children.Add(panel); return root;
        }

        private static WpfButton Button(string text, string automationId, string helpText, Action action)
        {
            var button = new WpfButton { Content = text, Margin = new Thickness(4), Padding = new Thickness(10, 4, 10, 4) };
            System.Windows.Automation.AutomationProperties.SetAutomationId(button, automationId);
            AutomationProperties.SetName(button, text);
            AutomationProperties.SetHelpText(button, helpText);
            button.Click += (_, _) => action(); return button;
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs args)
        {
            if (_controller.IsLocked) return;
            var point = PointToScreen(args.GetPosition(this));
            _controller.Begin((int)Math.Round(point.X), (int)Math.Round(point.Y));
            _isDragging = true;
            CaptureMouse(); SelectionChanged?.Invoke();
        }

        private void OnMouseMove(object sender, WpfMouseEventArgs args)
        {
            if (!_isDragging || args.LeftButton != MouseButtonState.Pressed) return;
            var point = PointToScreen(args.GetPosition(this));
            _controller.Update((int)Math.Round(point.X), (int)Math.Round(point.Y)); SelectionChanged?.Invoke();
        }

        private void OnKeyDown(object sender, WpfKeyEventArgs args)
        {
            if (args.Key == Key.Escape) CancelRequested?.Invoke();
            else if (args.Key == Key.Enter) ConfirmRequested?.Invoke();
            else if (args.Key == Key.R) ResetRequested?.Invoke();
            else if (args.Key == Key.L) { _controller.SetLocked(!_controller.IsLocked); SelectionChanged?.Invoke(); }
            else if (args.Key == Key.H) { _controller.SetHidden(!_controller.IsHidden); SelectionChanged?.Invoke(); }
            else if (args.Key == Key.Delete) { _controller.Delete(); SelectionChanged?.Invoke(); }
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
            _status.Text = _controller.Selection is { } current
                ? $"Región {current.Width} por {current.Height} píxeles. {(_controller.IsLocked ? "Bloqueada." : "Editable.")} {(_controller.IsHidden ? "Borde oculto." : "Borde visible.")}"
                : "Sin región seleccionada";
            if (_controller.IsHidden || _controller.Selection is not { } region) { _selection.Visibility = Visibility.Collapsed; return; }
            var intersection = ScreenCaptureGeometry.Intersect(region,
                new(_screen.Bounds.X, _screen.Bounds.Y, _screen.Bounds.Width, _screen.Bounds.Height));
            if (intersection.IsEmpty) { _selection.Visibility = Visibility.Collapsed; return; }
            var topLeft = PointFromScreen(new WpfPoint(intersection.X, intersection.Y));
            var bottomRight = PointFromScreen(new WpfPoint(intersection.Right, intersection.Bottom));
            Canvas.SetLeft(_selection, topLeft.X); Canvas.SetTop(_selection, topLeft.Y);
            _selection.Width = bottomRight.X - topLeft.X; _selection.Height = bottomRight.Y - topLeft.Y;
            _selection.Visibility = Visibility.Visible;
        }
    }
}
