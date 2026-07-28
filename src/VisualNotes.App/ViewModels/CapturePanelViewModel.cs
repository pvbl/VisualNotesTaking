using System.Windows;
using System.Windows.Input;

using VisualNotes.Core.Models;

namespace VisualNotes.App.ViewModels;

public enum CapturePanelMode { Monitor, Region, Window, Desktop }
public sealed record CaptureModeChoice(CapturePanelMode Mode, string Label, string Description);
public enum CapturePanelPlacement { Flotante, Izquierda, Derecha, Arriba, Abajo }
public enum ContextEditorPlacement { Dentro, Izquierda, Derecha, Debajo, Flotante }
public enum DraftChangeDecision { SaveAsNote, Discard, Cancel }
public sealed record CaptureDraft(string Title, string Tags, string ContextMarkdown)
{
    public bool IsEmpty => string.IsNullOrWhiteSpace(Title) &&
        string.IsNullOrWhiteSpace(Tags) && string.IsNullOrWhiteSpace(ContextMarkdown);
}

/// <summary>State and commands exposed by the always-on-top capture controller.</summary>
public sealed class CapturePanelViewModel : ViewModelBase
{
    private readonly MainViewModel _main;
    private readonly Func<bool, Task<bool>>? _setRegionLock;
    private CapturePanelMode _mode = CapturePanelMode.Monitor;
    private int _queuedCaptures;
    private int _captureCount;
    private bool _isMinimal;
    private double _panelOpacity = 0.94;
    private CapturePanelPlacement _placement = CapturePanelPlacement.Derecha;
    private ContextEditorPlacement _contextPlacement = ContextEditorPlacement.Dentro;
    private string _contextMarkdown = string.Empty;
    private string _captureTitle = string.Empty;
    private string _captureTags = string.Empty;
    private string _newSectionName = string.Empty;
    private bool _isRegionLocked;
    private bool _hasRegion;
    private string _selectedCourseName = string.Empty;
    private string _selectedModuleName = string.Empty;
    private NoteSession? _selectedSession;
    private readonly Func<Task<DraftChangeDecision>> _confirmDraftChange;

    public CapturePanelViewModel(
        MainViewModel main,
        Func<bool, Task<bool>>? setRegionLock = null,
        bool isRegionLocked = false,
        Func<Task<DraftChangeDecision>>? confirmDraftChange = null)
    {
        _main = main;
        _setRegionLock = setRegionLock;
        _confirmDraftChange = confirmDraftChange ?? (() => Task.FromResult(DraftChangeDecision.Discard));
        _isRegionLocked = isRegionLocked;
        CaptureCommand = new RelayCommand(_ => CaptureRequested?.Invoke(Mode, Draft));
        AddTextNoteCommand = new AsyncRelayCommand(async (_, cancellationToken) =>
        {
            if (Draft.IsEmpty) return;
            await _main.AddTextNoteAsync(Draft, cancellationToken);
            ClearDraft();
        });
        RunSessionBatchCommand = _main.Captures.RunSessionBatchCommand;
        AddSectionCommand = new AsyncRelayCommand(async (_, cancellationToken) =>
        {
            var (course, module) = ResolveAcademicContext();
            var section = await _main.AddSectionAsync(NewSectionName, course, module, cancellationToken);
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
        DefineRegionCommand = main.RedefineRegionCommand;
        TogglePauseCommand = main.TogglePauseCommand;
        NextSectionCommand = new RelayCommand(_ => ChangeSection(1));
        PreviousSectionCommand = new RelayCommand(_ => ChangeSection(-1));
        UndoCommand = main.UndoCommand;
        MarkImportantCommand = main.MarkImportantCommand;
        AddContextCommand = main.AddContextCommand;
        ToggleMinimalCommand = new RelayCommand(_ => IsMinimal = !IsMinimal);
        OpenReviewCommand = new RelayCommand(_ => _main.OpenActiveSessionReview());
        SelectPlacementCommand = new RelayCommand(value =>
        {
            if (value is CapturePanelPlacement placement) Placement = placement;
            else if (value is string text && Enum.TryParse<CapturePanelPlacement>(text, out var parsed)) Placement = parsed;
        });
        main.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName is nameof(MainViewModel.ActiveSession) or nameof(MainViewModel.ActiveSessionName)
                or nameof(MainViewModel.ActiveSectionName) or nameof(MainViewModel.SessionStatus)) RefreshSession();
        };
        RefreshSession();
    }

    public string SessionName => _main.ActiveSessionName;
    public string SectionName => _main.ActiveSectionName;
    public string SessionStatus => _main.SessionStatus;
    public IReadOnlyList<string> Courses => AcademicLocations()
        .Select(location => location.Course.Name).Distinct(StringComparer.CurrentCultureIgnoreCase)
        .OrderBy(x => x).ToArray();
    public IReadOnlyList<string> Modules => AcademicLocations()
        .Where(location => location.Course.Name.Equals(SelectedCourseName, StringComparison.CurrentCultureIgnoreCase))
        .Select(location => location.Module.Name).Distinct(StringComparer.CurrentCultureIgnoreCase)
        .OrderBy(x => x).ToArray();
    public IReadOnlyList<NoteSession> AvailableSessions => _main.Sessions.RecentSessions
        .OrderByDescending(session => session.ModifiedAt).ToArray();
    public string SelectedCourseName
    {
        get => _selectedCourseName;
        set
        {
            if (_selectedCourseName == value) return;
            _selectedCourseName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Modules));
            SelectedModuleName = Modules.FirstOrDefault() ?? string.Empty;
        }
    }
    public string SelectedModuleName
    {
        get => _selectedModuleName;
        set
        {
            if (_selectedModuleName == value) return;
            _selectedModuleName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AvailableSessions));
        }
    }
    public NoteSession? SelectedSession
    {
        get => _selectedSession;
        set
        {
            if (value is null || value == _selectedSession) return;
            _ = ChangeSessionAsync(value);
        }
    }
    public IReadOnlyList<NoteSection> Sections => _main.ActiveSession?.Sections.OrderBy(section => section.Order).ToArray() ?? [];
    public Guid? SelectedSectionId
    {
        get => _main.ActiveSession?.ActiveSectionId;
        set
        {
            if (value is not { } id || id == _main.ActiveSession?.ActiveSectionId) return;
            var section = Sections.FirstOrDefault(item => item.Id == id);
            if (section is not null)
            {
                _main.Sessions.ActivateSectionCommand.Execute(section);
                SelectAcademicContext(section);
            }
        }
    }
    public string NewSectionName { get => _newSectionName; set { _newSectionName = value; OnPropertyChanged(); } }
    public bool HasRegion => _hasRegion;
    public bool IsRegionLocked => _isRegionLocked;
    public string RegionStatus => !HasRegion ? "Región sin definir" :
        IsRegionLocked ? "Región bloqueada" : "Región lista";
    public string DefineRegionLabel => HasRegion ? "Redefinir región" : "Definir región";
    public string RegionLockLabel => IsRegionLocked ? "Desbloquear" : "Bloquear";
    public CapturePanelMode Mode { get => _mode; set { _mode = value; OnPropertyChanged(); } }
    public IReadOnlyList<CaptureModeChoice> Modes { get; } =
    [
        new(CapturePanelMode.Monitor, "Pantalla", "Monitor donde está el cursor"),
        new(CapturePanelMode.Region, "Región", "Área reutilizable de la pantalla"),
        new(CapturePanelMode.Window, "Ventana", "Ventana que elijas"),
        new(CapturePanelMode.Desktop, "Todos los monitores", "Escritorio virtual completo")
    ];
    public CapturePanelPlacement Placement
    {
        get => _placement;
        set
        {
            if (_placement == value) return;
            _placement = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsVerticalLayout));
            OnPropertyChanged(nameof(IsFloatingPlacement));
            OnPropertyChanged(nameof(IsLeftPlacement));
            OnPropertyChanged(nameof(IsRightPlacement));
            OnPropertyChanged(nameof(IsTopPlacement));
            OnPropertyChanged(nameof(IsBottomPlacement));
        }
    }
    public bool IsFloatingPlacement => Placement == CapturePanelPlacement.Flotante;
    public bool IsLeftPlacement => Placement == CapturePanelPlacement.Izquierda;
    public bool IsRightPlacement => Placement == CapturePanelPlacement.Derecha;
    public bool IsTopPlacement => Placement == CapturePanelPlacement.Arriba;
    public bool IsBottomPlacement => Placement == CapturePanelPlacement.Abajo;
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
    public bool IsVerticalLayout => Placement is CapturePanelPlacement.Derecha or CapturePanelPlacement.Izquierda;
    public int QueuedCaptures { get => _queuedCaptures; set { _queuedCaptures = Math.Max(0, value); OnPropertyChanged(); } }
    public int CaptureCount { get => _captureCount; private set { _captureCount = value; OnPropertyChanged(); } }
    public bool IsMinimal { get => _isMinimal; set { _isMinimal = value; OnPropertyChanged(); OnPropertyChanged(nameof(ExpandedVisibility)); } }
    public double PanelOpacity { get => _panelOpacity; set { _panelOpacity = Math.Clamp(value, 0.55, 1); OnPropertyChanged(); } }
    public string ContextMarkdown { get => _contextMarkdown; set { _contextMarkdown = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasDraft)); } }
    public string CaptureTitle { get => _captureTitle; set { _captureTitle = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasDraft)); } }
    public string CaptureTags { get => _captureTags; set { _captureTags = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasDraft)); } }
    public bool HasDraft => !Draft.IsEmpty;
    public CaptureDraft Draft => new(CaptureTitle, CaptureTags, ContextMarkdown);
    public Visibility ExpandedVisibility => IsMinimal ? Visibility.Collapsed : Visibility.Visible;
    public ICommand CaptureCommand { get; }
    public ICommand AddTextNoteCommand { get; }
    public ICommand RunSessionBatchCommand { get; }
    public ICommand AddSectionCommand { get; }
    public ICommand ToggleRegionLockCommand { get; }
    public ICommand DefineRegionCommand { get; }
    public ICommand TogglePauseCommand { get; }
    public ICommand NextSectionCommand { get; }
    public ICommand PreviousSectionCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand MarkImportantCommand { get; }
    public ICommand AddContextCommand { get; }
    public ICommand ToggleMinimalCommand { get; }
    public ICommand OpenReviewCommand { get; }
    public ICommand SelectPlacementCommand { get; }
    public event Action<CapturePanelMode, CaptureDraft>? CaptureRequested;

    public void CaptureCompleted(bool clearContext = false)
    {
        CaptureCount++;
        QueuedCaptures = Math.Max(0, QueuedCaptures - 1);
        if (clearContext) ClearDraft();
    }

    public void SetRegionState(bool hasRegion, bool isLocked)
    {
        if (_hasRegion == hasRegion && _isRegionLocked == isLocked) return;
        _hasRegion = hasRegion;
        _isRegionLocked = isLocked;
        OnPropertyChanged(nameof(HasRegion));
        OnPropertyChanged(nameof(IsRegionLocked));
        OnPropertyChanged(nameof(RegionStatus));
        OnPropertyChanged(nameof(DefineRegionLabel));
        OnPropertyChanged(nameof(RegionLockLabel));
    }

    public void SetRegionLockState(bool isLocked) => SetRegionState(true, isLocked);

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

        if (placement is CapturePanelPlacement.Derecha or CapturePanelPlacement.Izquierda)
        {
            var width = Math.Min(workArea.Width, Math.Clamp(Math.Round(workArea.Width / 6), 220, 480));
            var dockedLeft = placement == CapturePanelPlacement.Derecha ? workArea.Right - width : workArea.Left;
            return new(dockedLeft, workArea.Top, width, workArea.Height);
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
        _selectedSession = _main.ActiveSession;
        var activeSection = _selectedSession?.Sections.FirstOrDefault(section =>
            section.Id == _selectedSession.ActiveSectionId);
        _selectedCourseName = activeSection?.Course?.Name ??
            _selectedSession?.Course?.Name ?? Courses.FirstOrDefault() ?? "Sin clasificar";
        _selectedModuleName = activeSection?.CourseModule?.Name ??
            _selectedSession?.CourseModule?.Name ?? Modules.FirstOrDefault() ?? "Sin clasificar";
        OnPropertyChanged(nameof(SessionName));
        OnPropertyChanged(nameof(SectionName));
        OnPropertyChanged(nameof(SessionStatus));
        OnPropertyChanged(nameof(Sections));
        OnPropertyChanged(nameof(SelectedSectionId));
        OnPropertyChanged(nameof(Courses));
        OnPropertyChanged(nameof(SelectedCourseName));
        OnPropertyChanged(nameof(Modules));
        OnPropertyChanged(nameof(SelectedModuleName));
        OnPropertyChanged(nameof(AvailableSessions));
        OnPropertyChanged(nameof(SelectedSession));
        CaptureCount = _selectedSession?.Screenshots.Count(x => x.Status != EntityStatus.Deleted) ?? 0;
    }

    private async Task ChangeSessionAsync(NoteSession session)
    {
        if (HasDraft)
        {
            var decision = await _confirmDraftChange();
            if (decision == DraftChangeDecision.Cancel)
            {
                OnPropertyChanged(nameof(SelectedSession));
                return;
            }
            if (decision == DraftChangeDecision.SaveAsNote)
                await _main.AddTextNoteAsync(Draft);
            ClearDraft();
        }
        await _main.SwitchActiveSessionAsync(session);
        RefreshSession();
    }

    private void ClearDraft()
    {
        _captureTitle = string.Empty;
        _captureTags = string.Empty;
        _contextMarkdown = string.Empty;
        OnPropertyChanged(nameof(CaptureTitle));
        OnPropertyChanged(nameof(CaptureTags));
        OnPropertyChanged(nameof(ContextMarkdown));
        OnPropertyChanged(nameof(HasDraft));
    }

    private IReadOnlyList<(Course Course, CourseModule Module)> AcademicLocations()
    {
        var fromSections = _main.Sessions.RecentSessions.SelectMany(session => session.Sections)
            .Where(section => section.Course is not null && section.CourseModule is not null)
            .Select(section => (Course: section.Course!, Module: section.CourseModule!));
        var legacy = _main.Sessions.RecentSessions
            .Where(session => session.Course is not null && session.CourseModule is not null)
            .Select(session => (Course: session.Course!, Module: session.CourseModule!));
        return fromSections.Concat(legacy)
            .DistinctBy(location => (location.Item1.Id, location.Item2.Id)).ToArray();
    }

    private (Course Course, CourseModule Module) ResolveAcademicContext()
    {
        var courseName = string.IsNullOrWhiteSpace(SelectedCourseName) ? "Sin clasificar" : SelectedCourseName.Trim();
        var moduleName = string.IsNullOrWhiteSpace(SelectedModuleName) ? "Sin clasificar" : SelectedModuleName.Trim();
        var locations = AcademicLocations();
        var course = locations.Select(location => location.Course).FirstOrDefault(item =>
            item.Name.Equals(courseName, StringComparison.CurrentCultureIgnoreCase)) ??
            new Course { Name = courseName };
        var module = locations.Where(location => location.Course.Id == course.Id)
            .Select(location => location.Module).FirstOrDefault(item =>
                item.Name.Equals(moduleName, StringComparison.CurrentCultureIgnoreCase)) ??
            course.Modules.FirstOrDefault(item =>
                item.Name.Equals(moduleName, StringComparison.CurrentCultureIgnoreCase)) ??
            new CourseModule
            {
                CourseId = course.Id,
                Course = course,
                Name = moduleName,
                Order = course.Modules.Count
            };
        if (!course.Modules.Contains(module)) course.Modules.Add(module);
        return (course, module);
    }

    private void SelectAcademicContext(NoteSection section)
    {
        if (section.Course is not null) SelectedCourseName = section.Course.Name;
        if (section.CourseModule is not null) SelectedModuleName = section.CourseModule.Name;
    }
}
