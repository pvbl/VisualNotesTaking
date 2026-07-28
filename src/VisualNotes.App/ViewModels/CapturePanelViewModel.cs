using System.Windows;
using System.Windows.Input;

namespace VisualNotes.App.ViewModels;

public enum CapturePanelMode { Region, Monitor, Desktop, Window }
public enum CapturePanelPlacement { Flotante, Derecha, Arriba, Abajo }

/// <summary>State and commands exposed by the always-on-top capture controller.</summary>
public sealed class CapturePanelViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private CapturePanelMode _mode = CapturePanelMode.Region;
    private int _queuedCaptures;
    private int _captureCount;
    private bool _isMinimal;
    private double _panelOpacity = 0.94;
    private CapturePanelPlacement _placement;

    public CapturePanelViewModel(MainViewModel main)
    {
        _main = main;
        CaptureCommand = new RelayCommand(_ => CaptureRequested?.Invoke(Mode));
        TogglePauseCommand = main.TogglePauseCommand;
        NextSectionCommand = new RelayCommand(_ => ChangeSection(1));
        PreviousSectionCommand = new RelayCommand(_ => ChangeSection(-1));
        UndoCommand = main.UndoCommand;
        MarkImportantCommand = main.MarkImportantCommand;
        AddContextCommand = main.AddContextCommand;
        ToggleMinimalCommand = new RelayCommand(_ => IsMinimal = !IsMinimal);
        main.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.ActiveSession) or nameof(MainViewModel.ActiveSessionName)
                or nameof(MainViewModel.ActiveSectionName) or nameof(MainViewModel.SessionStatus)) RefreshSession();
        };
    }

    public string SessionName => _main.ActiveSessionName;
    public string SectionName => _main.ActiveSectionName;
    public string SessionStatus => _main.SessionStatus;
    public CapturePanelMode Mode { get => _mode; set { _mode = value; OnPropertyChanged(); } }
    public IReadOnlyList<CapturePanelMode> Modes { get; } = Enum.GetValues<CapturePanelMode>();
    public CapturePanelPlacement Placement
    {
        get => _placement;
        set
        {
            if (_placement == value) return;
            _placement = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsVerticalLayout));
        }
    }
    public IReadOnlyList<CapturePanelPlacement> Placements { get; } = Enum.GetValues<CapturePanelPlacement>();
    public bool IsVerticalLayout => Placement == CapturePanelPlacement.Derecha;
    public int QueuedCaptures { get => _queuedCaptures; set { _queuedCaptures = Math.Max(0, value); OnPropertyChanged(); } }
    public int CaptureCount { get => _captureCount; private set { _captureCount = value; OnPropertyChanged(); } }
    public bool IsMinimal { get => _isMinimal; set { _isMinimal = value; OnPropertyChanged(); OnPropertyChanged(nameof(ExpandedVisibility)); } }
    public double PanelOpacity { get => _panelOpacity; set { _panelOpacity = Math.Clamp(value, 0.55, 1); OnPropertyChanged(); } }
    public Visibility ExpandedVisibility => IsMinimal ? Visibility.Collapsed : Visibility.Visible;
    public ICommand CaptureCommand { get; }
    public ICommand TogglePauseCommand { get; }
    public ICommand NextSectionCommand { get; }
    public ICommand PreviousSectionCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand MarkImportantCommand { get; }
    public ICommand AddContextCommand { get; }
    public ICommand ToggleMinimalCommand { get; }
    public event Action<CapturePanelMode>? CaptureRequested;

    public void CaptureCompleted() { CaptureCount++; QueuedCaptures = Math.Max(0, QueuedCaptures - 1); }

    public static Rect ConstrainToWorkArea(Rect requested, Rect workArea)
    {
        var minimumWidth = Math.Min(220, workArea.Width);
        var width = Math.Clamp(requested.Width, minimumWidth, workArea.Width);
        var height = Math.Clamp(requested.Height, 56, workArea.Height);
        return new(Math.Clamp(requested.X, workArea.Left, workArea.Right - width),
            Math.Clamp(requested.Y, workArea.Top, workArea.Bottom - height), width, height);
    }

    public static Rect GetPlacementBounds(CapturePanelPlacement placement, Rect workArea, Rect floatingBounds)
    {
        if (placement == CapturePanelPlacement.Flotante)
            return ConstrainToWorkArea(floatingBounds, workArea);

        if (placement == CapturePanelPlacement.Derecha)
        {
            var width = Math.Min(workArea.Width, Math.Clamp(Math.Round(workArea.Width / 6), 220, 480));
            return new(workArea.Right - width, workArea.Top, width, workArea.Height);
        }

        var horizontalWidth = Math.Min(1040, workArea.Width);
        var horizontalHeight = Math.Min(190, workArea.Height);
        var left = workArea.Left + ((workArea.Width - horizontalWidth) / 2);
        var top = placement == CapturePanelPlacement.Arriba
            ? workArea.Top
            : workArea.Bottom - horizontalHeight;
        return new(left, top, horizontalWidth, horizontalHeight);
    }

    private void ChangeSection(int offset)
    {
        if (_main.ActiveSession is not { } session) return;
        var sections = session.Sections.OrderBy(section => section.Order).ToArray();
        if (sections.Length == 0) return;
        var current = Array.FindIndex(sections, section => section.Id == session.ActiveSectionId);
        _main.Sessions.ActivateSectionCommand.Execute(sections[Math.Clamp(current + offset, 0, sections.Length - 1)]);
    }

    private void RefreshSession()
    {
        OnPropertyChanged(nameof(SessionName)); OnPropertyChanged(nameof(SectionName)); OnPropertyChanged(nameof(SessionStatus));
    }
}
