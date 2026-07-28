using System.Windows;
using System.Windows.Input;

using VisualNotes.Core.Models;

namespace VisualNotes.App.ViewModels;

public enum CapturePanelMode { Region, Monitor, Desktop, Window }
public enum CapturePanelPlacement { Flotante, Derecha, Arriba, Abajo }
public enum ContextEditorPlacement { Dentro, Izquierda, Derecha, Debajo, Flotante }

/// <summary>State and commands exposed by the always-on-top capture controller.</summary>
public sealed class CapturePanelViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private readonly Func<bool, Task<bool>>? _setRegionLock;
    private CapturePanelMode _mode = CapturePanelMode.Region;
    private int _queuedCaptures;
    private int _captureCount;
    private bool _isMinimal;
    private double _panelOpacity = 0.94;
    private CapturePanelPlacement _placement = CapturePanelPlacement.Derecha;
    private ContextEditorPlacement _contextPlacement = ContextEditorPlacement.Dentro;
    private string _contextMarkdown = string.Empty;
    private string _newSectionName = string.Empty;
    private bool _isRegionLocked;

    public CapturePanelViewModel(
        MainViewModel main,
        Func<bool, Task<bool>>? setRegionLock = null,
        bool isRegionLocked = false)
    {
        _main = main;
        _setRegionLock = setRegionLock;
        _isRegionLocked = isRegionLocked;
        CaptureCommand = new RelayCommand(_ => CaptureRequested?.Invoke(Mode, ContextMarkdown));
        AddTextNoteCommand = new AsyncRelayCommand(async (_, cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(ContextMarkdown)) return;
            await _main.AddTextNoteAsync(ContextMarkdown, cancellationToken);
            ContextMarkdown = string.Empty;
        });
        RunSessionBatchCommand = _main.Captures.RunSessionBatchCommand;
        AddSectionCommand = new AsyncRelayCommand(async (_, cancellationToken) =>
        {
            var section = await _main.AddSectionAsync(NewSectionName, cancellationToken);
            NewSectionName = string.Empty;
            OnPropertyChanged(nameof(Sections));
            OnPropertyChanged(nameof(SelectedSectionId));
            OnPropertyChanged(nameof(SectionName));
        });
        ToggleRegionLockCommand = new AsyncRelayCommand(async (_, _) =>
        {
            var requested = !IsRegionLocked;
            if (_setRegionLock is not null && await _setRegionLock(requested))
                SetRegionLockState(requested);
        });
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
    public IReadOnlyList<NoteSection> Sections => _main.ActiveSession?.Sections.OrderBy(section => section.Order).ToArray() ?? [];
    public Guid? SelectedSectionId
    {
        get => _main.ActiveSession?.ActiveSectionId;
        set
        {
            if (value is not { } id || id == _main.ActiveSession?.ActiveSectionId) return;
            var section = Sections.FirstOrDefault(item => item.Id == id);
            if (section is not null) _main.Sessions.ActivateSectionCommand.Execute(section);
        }
    }
    public string NewSectionName { get => _newSectionName; set { _newSectionName = value; OnPropertyChanged(); } }
    public bool IsRegionLocked => _isRegionLocked;
    public string RegionLockLabel => IsRegionLocked ? "Desbloquear región" : "Bloquear región";
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
    public ContextEditorPlacement ContextPlacement
    {
        get => _contextPlacement;
        set
        {
            if (_contextPlacement == value) return;
            _contextPlacement = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ContextInsideVisibility));
        }
    }
    public IReadOnlyList<ContextEditorPlacement> ContextPlacements { get; } = Enum.GetValues<ContextEditorPlacement>();
    public Visibility ContextInsideVisibility => ContextPlacement == ContextEditorPlacement.Dentro
        ? Visibility.Visible
        : Visibility.Collapsed;
    public bool IsVerticalLayout => Placement == CapturePanelPlacement.Derecha;
    public int QueuedCaptures { get => _queuedCaptures; set { _queuedCaptures = Math.Max(0, value); OnPropertyChanged(); } }
    public int CaptureCount { get => _captureCount; private set { _captureCount = value; OnPropertyChanged(); } }
    public bool IsMinimal { get => _isMinimal; set { _isMinimal = value; OnPropertyChanged(); OnPropertyChanged(nameof(ExpandedVisibility)); } }
    public double PanelOpacity { get => _panelOpacity; set { _panelOpacity = Math.Clamp(value, 0.55, 1); OnPropertyChanged(); } }
    public string ContextMarkdown { get => _contextMarkdown; set { _contextMarkdown = value; OnPropertyChanged(); } }
    public Visibility ExpandedVisibility => IsMinimal ? Visibility.Collapsed : Visibility.Visible;
    public ICommand CaptureCommand { get; }
    public ICommand AddTextNoteCommand { get; }
    public ICommand RunSessionBatchCommand { get; }
    public ICommand AddSectionCommand { get; }
    public ICommand ToggleRegionLockCommand { get; }
    public ICommand TogglePauseCommand { get; }
    public ICommand NextSectionCommand { get; }
    public ICommand PreviousSectionCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand MarkImportantCommand { get; }
    public ICommand AddContextCommand { get; }
    public ICommand ToggleMinimalCommand { get; }
    public event Action<CapturePanelMode, string>? CaptureRequested;

    public void CaptureCompleted(bool clearContext = false)
    {
        CaptureCount++;
        QueuedCaptures = Math.Max(0, QueuedCaptures - 1);
        if (clearContext) ContextMarkdown = string.Empty;
    }

    public void SetRegionLockState(bool isLocked)
    {
        if (_isRegionLocked == isLocked) return;
        _isRegionLocked = isLocked;
        OnPropertyChanged(nameof(IsRegionLocked));
        OnPropertyChanged(nameof(RegionLockLabel));
    }

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
        var horizontalHeight = Math.Min(390, workArea.Height);
        var left = workArea.Left + ((workArea.Width - horizontalWidth) / 2);
        var top = placement == CapturePanelPlacement.Arriba
            ? workArea.Top
            : workArea.Bottom - horizontalHeight;
        return new(left, top, horizontalWidth, horizontalHeight);
    }

    public static Rect GetContextBounds(
        ContextEditorPlacement placement,
        Rect controlBounds,
        System.Windows.Size contextSize,
        Rect floatingBounds)
    {
        const double gap = 8;
        return placement switch
        {
            ContextEditorPlacement.Izquierda => new(
                controlBounds.Left - contextSize.Width - gap,
                controlBounds.Top,
                contextSize.Width,
                contextSize.Height),
            ContextEditorPlacement.Derecha => new(
                controlBounds.Right + gap,
                controlBounds.Top,
                contextSize.Width,
                contextSize.Height),
            ContextEditorPlacement.Debajo => new(
                controlBounds.Left,
                controlBounds.Bottom + gap,
                contextSize.Width,
                contextSize.Height),
            _ => floatingBounds
        };
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
        OnPropertyChanged(nameof(SessionName));
        OnPropertyChanged(nameof(SectionName));
        OnPropertyChanged(nameof(SessionStatus));
        OnPropertyChanged(nameof(Sections));
        OnPropertyChanged(nameof(SelectedSectionId));
    }
}
