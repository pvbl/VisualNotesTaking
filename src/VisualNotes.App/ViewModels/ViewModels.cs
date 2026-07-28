using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;

using VisualNotes.Core.Models;
using VisualNotes.Core.Services;

namespace VisualNotes.App.ViewModels;

public abstract class ViewModelBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter) ?? true;
    public void Execute(object? parameter) => execute(parameter);
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public interface INotificationService
{
    void ShowError(string message);
}

public sealed class AsyncRelayCommand : INotifyPropertyChanged, ICommand
{
    public static INotificationService? DefaultNotificationService { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;
    private readonly Func<object?, CancellationToken, Task> _execute;
    private readonly Predicate<object?>? _canExecute;
    private readonly INotificationService? _notifications;
    private readonly bool _allowConcurrentExecutions;
    private CancellationTokenSource? _cancellation;
    private bool _isRunning;

    public AsyncRelayCommand(Func<object?, CancellationToken, Task> execute,
        Predicate<object?>? canExecute = null, INotificationService? notifications = null,
        bool allowConcurrentExecutions = false)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
        _notifications = notifications ?? DefaultNotificationService;
        _allowConcurrentExecutions = allowConcurrentExecutions;
    }

    public event EventHandler? CanExecuteChanged;
    public bool IsRunning { get => _isRunning; private set { if (_isRunning == value) return; _isRunning = value; OnPropertyChanged(); RaiseCanExecuteChanged(); } }
    public bool CanExecute(object? parameter) => (_allowConcurrentExecutions || !IsRunning) && (_canExecute?.Invoke(parameter) ?? true);
    public async void Execute(object? parameter) => await ExecuteAsync(parameter);

    public async Task ExecuteAsync(object? parameter = null, CancellationToken cancellationToken = default)
    {
        if (!CanExecute(parameter)) return;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, AsyncCommandOperations.ShutdownToken);
        _cancellation = cancellation;
        IsRunning = true;
        var operation = ExecuteCoreAsync(parameter, cancellation.Token);
        AsyncCommandOperations.Track(operation);
        try { await operation; }
        finally
        {
            if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
            IsRunning = false;
        }
    }

    private async Task ExecuteCoreAsync(object? parameter, CancellationToken cancellationToken)
    {
        try { await _execute(parameter, cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception) { _notifications?.ShowError(exception.Message); }
    }

    public void Cancel() => _cancellation?.Cancel();
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public static class AsyncCommandOperations
{
    private static readonly object Gate = new();
    private static readonly HashSet<Task> Active = [];
    private static readonly CancellationTokenSource Shutdown = new();
    internal static CancellationToken ShutdownToken => Shutdown.Token;

    internal static void Track(Task task)
    {
        lock (Gate) Active.Add(task);
        _ = task.ContinueWith(completed => { lock (Gate) Active.Remove(completed); },
            CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    public static async Task CancelAndWaitAsync()
    {
        Shutdown.Cancel();
        Task[] tasks;
        lock (Gate) tasks = Active.ToArray();
        await Task.WhenAll(tasks);
    }
}

public interface ICaptureActionContract
{
    bool CanUndo { get; }
    bool CanMarkImportant { get; }
    bool CanAddContext { get; }
    Task UndoAsync();
    Task MarkImportantAsync();
    Task AddContextAsync();
    void CaptureCompleted(Screenshot capture);
    void SessionStateChanged();
    event EventHandler? CanExecuteChanged;
}

public sealed class MainViewModel : ViewModelBase
{
    private readonly SessionCoordinator _coordinator;
    private readonly ISessionRepository _repository;
    private ViewModelBase _currentViewModel;
    private NoteSession? _activeSession;

    private readonly SettingsViewModel? _settings;
    private readonly ICaptureWorkspace? _captureWorkspace;
    private readonly IDocumentExporter? _documentExporter;
    private readonly IExportInteraction? _exportInteraction;
    private readonly ICaptureActionContract? _captureActions;

    public MainViewModel(SessionCoordinator coordinator, ISessionRepository repository, IGlobalHotkeyService? hotkeys = null,
        IApiCredentialStore? credentials = null, ICaptureWorkspace? captureWorkspace = null,
        IDocumentExporter? documentExporter = null, IExportInteraction? exportInteraction = null,
        ISettingsRepository? settingsRepository = null, IUnitOfWork? unitOfWork = null,
        IAnalysisJobProcessor? analysisJobs = null, ICaptureActionContract? captureActions = null)
    {
        _coordinator = coordinator; _repository = repository;
        _captureWorkspace = captureWorkspace; _documentExporter = documentExporter; _exportInteraction = exportInteraction;
        _captureActions = captureActions;
        _settings = hotkeys is null ? null : new SettingsViewModel(hotkeys, credentials, settingsRepository, unitOfWork);
        Sessions = new SessionViewModel(coordinator, repository, Activate);
        Captures = new CapturesViewModel(workspace: captureWorkspace, activeSession: () => ActiveSession,
            analysisJobs: analysisJobs, settings: settingsRepository, unitOfWork: unitOfWork);
        Document = new DocumentViewModel();
        Document.ExportRequested += ExportDocument;
        _currentViewModel = Sessions;
        NavigateCommand = new AsyncRelayCommand(async (page, cancellationToken) =>
        {
            if (page as string == "Captures") await Captures.LoadAsync();
            if (page as string == "Document" && ActiveSession is not null && _captureWorkspace is not null)
                Document.ReplaceDocument(await _captureWorkspace.ComposeAsync(ActiveSession));
            CurrentViewModel = page switch
            {
                "Captures" => Captures,
                "Instructions" => Instructions,
                "Document" => Document,
                "Settings" => _settings ?? Settings,
                _ => Sessions
            };
        });
        TogglePauseCommand = new AsyncRelayCommand(async (_, cancellationToken) =>
        {
            if (ActiveSession is null) return;
            var pause = !ActiveSession.IsPaused;
            await _coordinator.SetPausedAsync(ActiveSession, pause, cancellationToken);
            RefreshHeader();
            _captureActions?.SessionStateChanged();
            if (!pause) SessionActivated?.Invoke();
        });
        CaptureRegionCommand = new AsyncRelayCommand(async (_, _) => await (CaptureRegionRequested?.Invoke() ?? Task.CompletedTask));
        RedefineRegionCommand = new AsyncRelayCommand(async (_, _) => await (RedefineRegionRequested?.Invoke() ?? Task.CompletedTask));
        UndoCommand = new AsyncRelayCommand(async (_, cancellationToken) => await (_captureActions?.UndoAsync() ?? Task.CompletedTask), _ => _captureActions?.CanUndo == true);
        MarkImportantCommand = new AsyncRelayCommand(async (_, cancellationToken) => await (_captureActions?.MarkImportantAsync() ?? Task.CompletedTask), _ => _captureActions?.CanMarkImportant == true);
        AddContextCommand = new AsyncRelayCommand(async (_, cancellationToken) => await (_captureActions?.AddContextAsync() ?? Task.CompletedTask), _ => _captureActions?.CanAddContext == true);
        if (_captureActions is not null) _captureActions.CanExecuteChanged += (_, _) => RefreshCaptureActions();
    }

    public SessionViewModel Sessions { get; }
    public CapturesViewModel Captures { get; }
    public DocumentViewModel Document { get; }
    public InstructionsViewModel Instructions { get; } = new();
    public SettingsViewModel Settings { get; } = new();
    public ViewModelBase CurrentViewModel { get => _currentViewModel; private set { _currentViewModel = value; OnPropertyChanged(); } }
    public NoteSession? ActiveSession { get => _activeSession; private set { _activeSession = value; OnPropertyChanged(); RefreshHeader(); } }
    public string ActiveSessionName => ActiveSession?.Name ?? "Sin sesión activa";
    public string ActiveSectionName => ActiveSession?.Sections.FirstOrDefault(x => x.Id == ActiveSession.ActiveSectionId)?.Title ?? "Sin sección";
    public string SessionStatus => ActiveSession is null ? "Crea o continúa una sesión" : ActiveSession.IsPaused ? "Sesión pausada" : "Sesión activa";
    public ICommand NavigateCommand { get; }
    public ICommand TogglePauseCommand { get; }
    public ICommand CaptureRegionCommand { get; }
    public ICommand RedefineRegionCommand { get; }
    public AsyncRelayCommand UndoCommand { get; }
    public AsyncRelayCommand MarkImportantCommand { get; }
    public AsyncRelayCommand AddContextCommand { get; }
    public event Func<Task>? CaptureRegionRequested;
    public event Func<Task>? RedefineRegionRequested;
    public event Action? SessionActivated;
    public event Action? ReviewRequested;

    public async Task InitializeAsync()
    {
        if (_settings is not null) { await _settings.LoadCredentialsAsync(); await _settings.LoadSettingsAsync(); }
        await Sessions.LoadAsync();
    }

    public async Task<NoteSession> EnsureActiveSessionAsync(CancellationToken cancellationToken = default)
    {
        var session = ActiveSession ?? await Sessions.EnsureSelectedSessionAsync(cancellationToken);
        if (session.ActiveSectionId is not { } activeSectionId ||
            session.Sections.All(section => section.Id != activeSectionId))
        {
            var section = session.Sections.OrderBy(item => item.Order).FirstOrDefault();
            if (section is null)
                section = await _coordinator.AddSectionAsync(session, "General",
                    course: session.Course, courseModule: session.CourseModule, ct: cancellationToken);
            else
                await _coordinator.ActivateSectionAsync(session, section.Id, cancellationToken);
            Sessions.SelectedSection = section;
            RefreshHeader();
        }
        return session;
    }

    public async Task CaptureAddedAsync(Screenshot capture)
    {
        if (ActiveSession is not null && ActiveSession.Screenshots.All(item => item.Id != capture.Id))
            ActiveSession.Screenshots.Add(capture);
        Captures.AddCapture(capture);
        _captureActions?.CaptureCompleted(capture);
        if (_captureWorkspace is not null) await Captures.LoadAsync();
    }

    public async Task AddTextNoteAsync(CaptureDraft draft, CancellationToken cancellationToken = default)
    {
        if (draft.IsEmpty) return;
        var session = await EnsureActiveSessionAsync(cancellationToken);
        await ResumeActiveSessionAsync(cancellationToken);
        var note = new Screenshot
        {
            SessionId = session.Id,
            SectionId = session.ActiveSectionId,
            CapturedAt = DateTimeOffset.UtcNow,
            ProcessingStatus = ScreenshotStatus.Ready,
            DisplayTitle = draft.Title.Trim(),
            Tags = draft.Tags.Trim(),
            UserContext = draft.ContextMarkdown.Trim()
        };
        await _coordinator.AddCaptureAsync(session, note, cancellationToken);
        await CaptureAddedAsync(note);
    }

    public async Task SwitchActiveSessionAsync(NoteSession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        Sessions.SelectedSession = session;
        await _coordinator.ContinueAsync(session, cancellationToken);
        RefreshHeader();
        SessionActivated?.Invoke();
    }

    public void OpenActiveSessionReview()
    {
        NavigateCommand.Execute("Captures");
        ReviewRequested?.Invoke();
    }

    public async Task<NoteSection> AddSectionAsync(
        string title,
        Course? course = null,
        CourseModule? courseModule = null,
        CancellationToken cancellationToken = default)
    {
        var session = await EnsureActiveSessionAsync(cancellationToken);
        var section = await _coordinator.AddSectionAsync(session,
            string.IsNullOrWhiteSpace(title) ? "Nueva sección" : title.Trim(),
            course: course,
            courseModule: courseModule,
            ct: cancellationToken);
        Sessions.SelectedSection = section;
        RefreshHeader();
        return section;
    }

    public async Task ResumeActiveSessionAsync(CancellationToken cancellationToken = default)
    {
        if (ActiveSession is not { IsPaused: true } session) return;
        await _coordinator.ContinueAsync(session, cancellationToken);
        RefreshHeader();
        _captureActions?.SessionStateChanged();
        SessionActivated?.Invoke();
    }

    private async void Activate(NoteSession session)
    {
        ActiveSession = session;
        _captureActions?.SessionStateChanged();
        SessionActivated?.Invoke();
        await Captures.LoadAsync();
    }
    private async Task ExportDocument(SemanticDocument document, ExportScope _)
    {
        if (_documentExporter is null || _exportInteraction is null) return;
        var path = _exportInteraction.SelectDocxPath(ActiveSession?.PlannedDocumentName);
        if (path is null) return;
        try
        {
            var exported = await _documentExporter.ExportAsync(document, path);
            _exportInteraction.ShowExportSucceeded(exported);
        }
        catch (Exception exception) { _exportInteraction.ShowExportFailed(exception.Message); }
    }
    public void RefreshHeader() { OnPropertyChanged(nameof(ActiveSessionName)); OnPropertyChanged(nameof(ActiveSectionName)); OnPropertyChanged(nameof(SessionStatus)); }
    private void RefreshCaptureActions() { UndoCommand.RaiseCanExecuteChanged(); MarkImportantCommand.RaiseCanExecuteChanged(); AddContextCommand.RaiseCanExecuteChanged(); }
}

public sealed class SessionViewModel : ViewModelBase
{
    private readonly SessionCoordinator _coordinator;
    private readonly ISessionRepository _repository;
    private readonly Action<NoteSession> _activate;
    private NoteSession _draft = NewDraft();
    private NoteSession? _selected;
    private NoteSection? _selectedSection;
    private string _lastActionMessage = string.Empty;
    private string _draftCourseName = "Sin clasificar";
    private string _draftModuleName = "Sin clasificar";
    private readonly SemaphoreSlim _automaticSessionGate = new(1, 1);

    public SessionViewModel(SessionCoordinator coordinator, ISessionRepository repository, Action<NoteSession> activate)
    {
        _coordinator = coordinator; _repository = repository; _activate = activate;
        CreateCommand = new AsyncRelayCommand(async (_, cancellationToken) =>
        {
            PrepareHierarchy(Draft);
            var session = await _coordinator.CreateAsync(Draft, ct: cancellationToken);
            RecentSessions.Insert(0, session);
            SelectedSession = session;
            ResetDraft();
        });
        DuplicateCommand = new AsyncRelayCommand(async (_, cancellationToken) => { if (SelectedSession is null) return; var copy = await _coordinator.CreateAsync(NewDraft(), SelectedSession.Id); RecentSessions.Insert(0, copy); SelectedSession = copy; });
        ContinueCommand = new AsyncRelayCommand(async (_, cancellationToken) => { if (SelectedSession is null) return; await _coordinator.ContinueAsync(SelectedSession); _activate(SelectedSession); });
        SaveCommand = new AsyncRelayCommand(async (_, cancellationToken) => { if (SelectedSession is not null) await _coordinator.SetPausedAsync(SelectedSession, SelectedSession.IsPaused); });
        AddSectionCommand = new AsyncRelayCommand(async (_, cancellationToken) =>
        {
            var session = await EnsureSelectedSessionAsync(cancellationToken);
            SelectedSection = await _coordinator.AddSectionAsync(session, "Nueva sección",
                course: SelectedSection?.Course ?? session.Course,
                courseModule: SelectedSection?.CourseModule ?? session.CourseModule,
                ct: cancellationToken);
            _activate(session);
        });
        AddSubsectionCommand = new AsyncRelayCommand(async (_, cancellationToken) =>
        {
            if (SelectedSection is null) return;
            var session = await EnsureSelectedSessionAsync(cancellationToken);
            SelectedSection = await _coordinator.AddSectionAsync(session, "Nueva subsección",
                parentId: SelectedSection.Id,
                course: SelectedSection.Course ?? session.Course,
                courseModule: SelectedSection.CourseModule ?? session.CourseModule,
                ct: cancellationToken);
            _activate(session);
        });
        ActivateSectionCommand = new AsyncRelayCommand(async (value, cancellationToken) => { if (SelectedSession is null || value is not NoteSection section) return; await _coordinator.ActivateSectionAsync(SelectedSession, section.Id); SelectedSection = section; _activate(SelectedSession); });
        RenameSectionCommand = new AsyncRelayCommand(async (_, cancellationToken) => { if (SelectedSection is not null) await _coordinator.RenameSectionAsync(SelectedSection, SelectedSection.Title); });
        MoveUpCommand = new AsyncRelayCommand(async (_, cancellationToken) => { if (SelectedSession is null || SelectedSection is null) return; await _coordinator.ReorderSectionAsync(SelectedSession, SelectedSection.Id, SelectedSection.Order - 1); OnPropertyChanged(nameof(OrderedSections)); });
        MoveDownCommand = new AsyncRelayCommand(async (_, cancellationToken) => { if (SelectedSession is null || SelectedSection is null) return; await _coordinator.ReorderSectionAsync(SelectedSession, SelectedSection.Id, SelectedSection.Order + 1); OnPropertyChanged(nameof(OrderedSections)); });
    }

    public ObservableCollection<NoteSession> RecentSessions { get; } = [];
    public NoteSession Draft { get => _draft; set { _draft = value; OnPropertyChanged(); } }
    public NoteSession? SelectedSession { get => _selected; set { _selected = value; OnPropertyChanged(); OnPropertyChanged(nameof(OrderedSections)); if (value is not null) _activate(value); } }
    public NoteSection? SelectedSection { get => _selectedSection; set { _selectedSection = value; OnPropertyChanged(); } }
    public string LastActionMessage { get => _lastActionMessage; private set { _lastActionMessage = value; OnPropertyChanged(); } }
    public string DraftCourseName { get => _draftCourseName; set { _draftCourseName = value; OnPropertyChanged(); } }
    public string DraftModuleName
    {
        get => _draftModuleName;
        set { _draftModuleName = value; Draft.Module = value; OnPropertyChanged(); }
    }
    public IEnumerable<NoteSection> OrderedSections => SelectedSession is null ? [] : SelectedSession.Sections.OrderBy(x => x.Order);
    public ICommand CreateCommand { get; }
    public ICommand DuplicateCommand { get; }
    public ICommand ContinueCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand AddSectionCommand { get; }
    public ICommand AddSubsectionCommand { get; }
    public ICommand ActivateSectionCommand { get; }
    public ICommand RenameSectionCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }

    public async Task LoadAsync() { RecentSessions.Clear(); foreach (var session in await _repository.ListAsync()) RecentSessions.Add(session); }

    public async Task<NoteSession> EnsureSelectedSessionAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedSession is not null) return SelectedSession;
        await _automaticSessionGate.WaitAsync(cancellationToken);
        try
        {
            if (SelectedSession is not null) return SelectedSession;
            PrepareHierarchy(Draft);
            Draft.Name = BuildAutomaticSessionName(Draft, DateTimeOffset.Now, RecentSessions.Select(session => session.Name));
            var session = await _coordinator.CreateAsync(Draft, ct: cancellationToken);
            RecentSessions.Insert(0, session);
            SelectedSession = session;
            ResetDraft();
            LastActionMessage = $"Se ha creado automáticamente la sesión «{session.Name}».";
            return session;
        }
        finally
        {
            _automaticSessionGate.Release();
        }
    }

    public static string BuildAutomaticSessionName(NoteSession draft, DateTimeOffset now, IEnumerable<string> existingNames)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(existingNames);
        var title = string.IsNullOrWhiteSpace(draft.Name) ||
            draft.Name.Equals("Nueva sesión", StringComparison.OrdinalIgnoreCase)
            ? (string.IsNullOrWhiteSpace(draft.Topic) ? "Sesión" : draft.Topic)
            : draft.Name;
        var parts = new[] { now.ToString("yyyy-MM-dd"), title,
                draft.Module.Equals("Sin clasificar", StringComparison.OrdinalIgnoreCase) ? string.Empty : draft.Module }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(SanitizeNamePart);
        var baseName = string.Join("_", parts);
        var names = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(baseName)) return baseName;
        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseName}_{suffix}";
            if (!names.Contains(candidate)) return candidate;
        }
    }

    private static string SanitizeNamePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string(value.Trim().Select(character =>
            invalid.Contains(character) || char.IsWhiteSpace(character) ? '_' : character).ToArray());
        while (cleaned.Contains("__", StringComparison.Ordinal)) cleaned = cleaned.Replace("__", "_", StringComparison.Ordinal);
        return cleaned.Trim('_');
    }

    private static NoteSession NewDraft() => new() { WorkingFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), PlannedDocumentName = "Apuntes.md" };

    private void PrepareHierarchy(NoteSession draft)
    {
        var courseName = string.IsNullOrWhiteSpace(DraftCourseName) ? "Sin clasificar" : DraftCourseName.Trim();
        var moduleName = !string.IsNullOrWhiteSpace(draft.Module) &&
            !draft.Module.Equals("Sin clasificar", StringComparison.OrdinalIgnoreCase)
            ? draft.Module.Trim()
            : string.IsNullOrWhiteSpace(DraftModuleName) ? "Sin clasificar" : DraftModuleName.Trim();
        var existingCourse = RecentSessions.Select(x => x.Course).FirstOrDefault(x =>
            x is not null && x.Name.Equals(courseName, StringComparison.CurrentCultureIgnoreCase));
        var course = existingCourse ?? new Course { Name = courseName };
        var module = RecentSessions.Where(x => x.Course?.Id == course.Id).Select(x => x.CourseModule)
            .FirstOrDefault(x => x is not null && x.Name.Equals(moduleName, StringComparison.CurrentCultureIgnoreCase))
            ?? course.Modules.FirstOrDefault(x => x.Name.Equals(moduleName, StringComparison.CurrentCultureIgnoreCase))
            ?? new CourseModule { CourseId = course.Id, Course = course, Name = moduleName, Order = course.Modules.Count };
        if (!course.Modules.Contains(module)) course.Modules.Add(module);
        draft.CourseId = course.Id;
        draft.Course = course;
        draft.CourseModuleId = module.Id;
        draft.CourseModule = module;
        draft.Module = module.Name;
    }

    private void ResetDraft()
    {
        Draft = NewDraft();
        DraftCourseName = "Sin clasificar";
        DraftModuleName = "Sin clasificar";
    }
}

public enum ReviewWorkspaceTab { Entries, MarkdownPreview, Result }

public sealed class CapturesViewModel : ViewModelBase
{
    private CaptureLibrary _library;
    private readonly ICaptureWorkspace? _workspace;
    private readonly Func<NoteSession?>? _activeSession;
    private readonly IAnalysisJobProcessor? _analysisJobs;
    private readonly ISettingsRepository? _settings;
    private readonly IUnitOfWork? _unitOfWork;
    private Screenshot? _selectedCapture;
    private bool _isQuickContextOpen;
    private Guid? _sectionFilter;
    private Guid? _targetSectionId;
    private string _tagFilter = string.Empty;
    private ScreenshotStatus? _statusFilter;
    private CaptureImportance? _importanceFilter;
    private ReviewFilter _reviewFilter;
    private bool _showTrash;
    private string _previewMarkdown = string.Empty;
    private string _resultMarkdown = string.Empty;
    private ReviewWorkspaceTab _activeTab;
    private string _saveStatus = string.Empty;
    private string _batchStatus = string.Empty;
    private string _resultStatus = string.Empty;

    public CapturesViewModel(IEnumerable<Screenshot>? captures = null, ICaptureWorkspace? workspace = null,
        Func<NoteSession?>? activeSession = null, IAnalysisJobProcessor? analysisJobs = null,
        ISettingsRepository? settings = null, IUnitOfWork? unitOfWork = null)
    {
        _workspace = workspace; _activeSession = activeSession;
        _analysisJobs = analysisJobs;
        _settings = settings;
        _unitOfWork = unitOfWork;
        _library = new CaptureLibrary(captures ?? []);
        Sections = _library.Captures.Where(capture => capture.Section is not null).Select(capture => capture.Section!).DistinctBy(section => section.Id).OrderBy(section => section.Order).ToArray();
        SelectedCaptures.CollectionChanged += (_, _) => OnPropertyChanged(nameof(SelectionCount));
        ApplyChipCommand = new AsyncRelayCommand(async (value, cancellationToken) => { if (SelectedCapture is null || value is not string chip) return; SelectedCapture.Tags = string.Join(", ", SelectedCapture.Tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Append(chip).Distinct(StringComparer.OrdinalIgnoreCase)); SelectedCapture.CaptureInstruction = CaptureInstructionResolver.Resolve([chip], SelectedCapture.CaptureInstruction); OnPropertyChanged(nameof(SelectedCapture)); await PersistAndRefreshAsync([SelectedCapture]); });
        CloseQuickContextCommand = new RelayCommand(_ => IsQuickContextOpen = false);
        ReanalyzeCommand = new AsyncRelayCommand(async (_, cancellationToken) => await QueueAnalysisAsync());
        RegenerateNoteCommand = new AsyncRelayCommand(async (_, cancellationToken) => await QueueAnalysisAsync());
        RunSessionBatchCommand = new AsyncRelayCommand(async (_, cancellationToken) => await RunSessionBatchAsync(cancellationToken));
        SaveSelectedCommand = new AsyncRelayCommand(async (_, cancellationToken) => await SaveSelectedAsync(cancellationToken));
        SaveResultCommand = new AsyncRelayCommand(async (_, cancellationToken) => await SaveResultAsync(cancellationToken));
        ShowEntriesCommand = new RelayCommand(_ => ActiveTab = ReviewWorkspaceTab.Entries);
        ShowPreviewCommand = new RelayCommand(_ => { RefreshMarkdown(); ActiveTab = ReviewWorkspaceTab.MarkdownPreview; });
        ShowResultCommand = new RelayCommand(_ => ActiveTab = ReviewWorkspaceTab.Result);
        CancelAnalysisCommand = new AsyncRelayCommand(async (value, cancellationToken) => { if (_analysisJobs is not null && value is AnalysisJob job) { await _analysisJobs.CancelAsync(job.Id); await LoadAsync(); } });
        RetryAnalysisCommand = new AsyncRelayCommand(async (value, cancellationToken) => { if (_analysisJobs is not null && value is AnalysisJob job) { await _analysisJobs.RetryAsync(job.Id); await _analysisJobs.RunManualAsync(); await LoadAsync(); } });
        ExcludeCommand = new AsyncRelayCommand(async (_, cancellationToken) => await RunAsync(_library.Exclude));
        DeleteCommand = new AsyncRelayCommand(async (_, cancellationToken) => await RunAsync(_library.Delete));
        RestoreCommand = new AsyncRelayCommand(async (_, cancellationToken) => await RunAsync(_library.Restore));
        UndoCommand = new AsyncRelayCommand(async (_, cancellationToken) => { if (_library.Undo()) await PersistAndRefreshAsync(_library.Captures); });
        MoveToSectionCommand = new AsyncRelayCommand(async (value, cancellationToken) => { if (value is Guid id) await RunAsync(ids => _library.MoveToSection(ids, id)); });
        ReorderCommand = new AsyncRelayCommand(async (value, cancellationToken) => { if (value is int index) await RunAsync(ids => _library.Reorder(ids, index)); });
        ClearFiltersCommand = new RelayCommand(_ => { SectionFilter = null; TagFilter = string.Empty; StatusFilter = null; ImportanceFilter = null; ReviewFilter = VisualNotes.Core.Services.ReviewFilter.All; });
        Refresh();
    }

    public ObservableCollection<Screenshot> Captures { get; } = [];
    public ObservableCollection<Screenshot> SelectedCaptures { get; } = [];
    public IReadOnlyCollection<string> QuickChips => CaptureInstructionResolver.QuickChips;
    public IReadOnlyList<NoteSection> Sections { get; private set; }
    public IReadOnlyList<ScreenshotStatus> Statuses { get; } = Enum.GetValues<ScreenshotStatus>();
    public IReadOnlyList<CaptureImportance> Importances { get; } = Enum.GetValues<CaptureImportance>();
    public IReadOnlyList<ReviewFilter> ReviewOptions { get; } = Enum.GetValues<ReviewFilter>();
    public int SelectionCount => SelectedCaptures.Count;
    public Screenshot? SelectedCapture { get => _selectedCapture; set { _selectedCapture = value; OnPropertyChanged(); IsQuickContextOpen = value is not null; } }
    public bool IsQuickContextOpen { get => _isQuickContextOpen; set { _isQuickContextOpen = value; OnPropertyChanged(); } }
    public Guid? SectionFilter { get => _sectionFilter; set { _sectionFilter = value; OnPropertyChanged(); Refresh(); } }
    public Guid? TargetSectionId { get => _targetSectionId; set { _targetSectionId = value; OnPropertyChanged(); } }
    public string TagFilter { get => _tagFilter; set { _tagFilter = value; OnPropertyChanged(); Refresh(); } }
    public ScreenshotStatus? StatusFilter { get => _statusFilter; set { _statusFilter = value; OnPropertyChanged(); Refresh(); } }
    public CaptureImportance? ImportanceFilter { get => _importanceFilter; set { _importanceFilter = value; OnPropertyChanged(); Refresh(); } }
    public ReviewFilter ReviewFilter { get => _reviewFilter; set { _reviewFilter = value; OnPropertyChanged(); Refresh(); } }
    public bool ShowTrash { get => _showTrash; set { _showTrash = value; OnPropertyChanged(); Refresh(); } }
    public string PreviewMarkdown { get => _previewMarkdown; private set { _previewMarkdown = value; OnPropertyChanged(); } }
    public string ResultMarkdown { get => _resultMarkdown; set { _resultMarkdown = value; OnPropertyChanged(); } }
    public ReviewWorkspaceTab ActiveTab
    {
        get => _activeTab;
        set { _activeTab = value; OnPropertyChanged(); OnPropertyChanged(nameof(ActiveTabIndex)); }
    }
    public int ActiveTabIndex
    {
        get => (int)ActiveTab;
        set { if (Enum.IsDefined(typeof(ReviewWorkspaceTab), value)) ActiveTab = (ReviewWorkspaceTab)value; }
    }
    public string SaveStatus { get => _saveStatus; private set { _saveStatus = value; OnPropertyChanged(); } }
    public string BatchStatus { get => _batchStatus; private set { _batchStatus = value; OnPropertyChanged(); } }
    public string ResultStatus { get => _resultStatus; private set { _resultStatus = value; OnPropertyChanged(); } }
    public int IncludedCount => _library.Captures.Count(x => x.Status != EntityStatus.Deleted && x.IncludeInDocument);
    public ICommand ApplyChipCommand { get; }
    public ICommand CloseQuickContextCommand { get; }
    public ICommand ReanalyzeCommand { get; }
    public ICommand RegenerateNoteCommand { get; }
    public ICommand RunSessionBatchCommand { get; }
    public ICommand SaveSelectedCommand { get; }
    public ICommand SaveResultCommand { get; }
    public ICommand ShowEntriesCommand { get; }
    public ICommand ShowPreviewCommand { get; }
    public ICommand ShowResultCommand { get; }
    public ICommand CancelAnalysisCommand { get; }
    public ICommand RetryAnalysisCommand { get; }
    public ICommand ExcludeCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand RestoreCommand { get; }
    public ICommand UndoCommand { get; }
    public ICommand MoveToSectionCommand { get; }
    public ICommand ReorderCommand { get; }
    public ICommand ClearFiltersCommand { get; }

    public void ReplaceSelection(IEnumerable<Screenshot> selection)
    {
        SelectedCaptures.Clear();
        foreach (var capture in selection) SelectedCaptures.Add(capture);
        SelectedCapture = SelectedCaptures.LastOrDefault();
    }

    public void AddCapture(Screenshot capture)
    {
        _library.Add(capture);
        Refresh();
    }

    public async Task LoadAsync()
    {
        if (_workspace is null || _activeSession?.Invoke() is not { } session) return;
        var selectedIds = SelectedCaptures.Select(x => x.Id).Append(SelectedCapture?.Id ?? Guid.Empty).ToHashSet();
        _library = new(await _workspace.LoadAsync(session.Id));
        Sections = session.Sections.OrderBy(x => x.Order).ToArray();
        OnPropertyChanged(nameof(Sections));
        Refresh();
        ReplaceSelection(Captures.Where(x => selectedIds.Contains(x.Id)));
        if (_settings is not null)
            ResultMarkdown = await _settings.GetAsync<string>(ResultKey(session.Id)) ?? ResultMarkdown;
    }

    private IReadOnlyCollection<Guid> SelectedIds() => Selected().Select(capture => capture.Id).ToArray();
    private IEnumerable<Screenshot> Selected() => SelectedCaptures.Count == 0 && SelectedCapture is not null ? [SelectedCapture] : SelectedCaptures;
    private async Task RunAsync(Action<IReadOnlyCollection<Guid>> action)
    {
        var changed = Selected().ToArray();
        action(SelectedIds());
        await PersistAndRefreshAsync(changed);
    }
    private async Task QueueAnalysisAsync()
    {
        if (_analysisJobs is null) { await RunAsync(_library.Reprocess); return; }
        var visualCaptures = Selected().Where(capture => capture.Image is not null).ToArray();
        foreach (var capture in visualCaptures)
        {
            capture.ProcessingStatus = ScreenshotStatus.Queued;
            await _analysisJobs.EnqueueAsync(new AnalysisJob
            {
                ScreenshotId = capture.Id,
                Trigger = AnalysisJobTrigger.Manual,
                IdempotencyKey = $"manual:{capture.Id:N}:{Guid.NewGuid():N}"
            });
        }
        await PersistAndRefreshAsync(visualCaptures);
        await _analysisJobs.RunManualAsync();
        await LoadAsync();
    }

    private async Task RunSessionBatchAsync(CancellationToken cancellationToken)
    {
        if (_analysisJobs is null || _activeSession?.Invoke() is not { } session || _workspace is null) return;
        BatchStatus = "Preparando elementos…";
        var captures = (await _workspace.LoadAsync(session.Id, cancellationToken))
            .Where(capture => capture.Status != EntityStatus.Deleted &&
                capture.IncludeInDocument &&
                capture.Image is not null &&
                capture.ProcessingStatus is not (ScreenshotStatus.Analyzing or ScreenshotStatus.Queued))
            .ToArray();
        foreach (var capture in captures)
        {
            capture.ProcessingStatus = ScreenshotStatus.Queued;
            await _analysisJobs.EnqueueAsync(new AnalysisJob
            {
                ScreenshotId = capture.Id,
                Trigger = AnalysisJobTrigger.Batch,
                IdempotencyKey = $"batch:{session.Id:N}:{capture.Id:N}:{Guid.NewGuid():N}"
            }, cancellationToken);
        }
        await _workspace.SaveAsync(captures, cancellationToken);
        BatchStatus = $"Procesando {captures.Length} capturas…";
        await _analysisJobs.RunBatchAsync(session.Id, cancellationToken);
        await LoadAsync();
        var document = await _workspace.ComposeAsync(session, cancellationToken);
        ResultMarkdown = MarkdownComposition.ComposeDocument(document);
        await SaveResultAsync(cancellationToken);
        BatchStatus = "Evaluación final completada";
        ActiveTab = ReviewWorkspaceTab.Result;
    }
    private async Task SaveSelectedAsync(CancellationToken cancellationToken)
    {
        if (_workspace is null || SelectedCapture is null) return;
        SaveStatus = "Guardando…";
        await _workspace.SaveAsync([SelectedCapture], cancellationToken);
        RefreshMarkdown();
        SaveStatus = $"Guardado {DateTime.Now:HH:mm:ss}";
    }
    private async Task SaveResultAsync(CancellationToken cancellationToken)
    {
        if (_settings is null || _activeSession?.Invoke() is not { } session) return;
        ResultStatus = "Guardando…";
        await _settings.SetAsync(ResultKey(session.Id), ResultMarkdown, cancellationToken: cancellationToken);
        if (_unitOfWork is not null) await _unitOfWork.SaveChangesAsync(cancellationToken);
        ResultStatus = $"Guardado {DateTime.Now:HH:mm:ss}";
    }
    private async Task PersistAndRefreshAsync(IReadOnlyCollection<Screenshot> changed)
    {
        if (_workspace is not null) await _workspace.SaveAsync(changed);
        Refresh();
    }
    private void Refresh()
    {
        var selectedIds = SelectedCaptures.Select(capture => capture.Id).ToHashSet();
        Captures.Clear();
        foreach (var capture in _library.Query(new(SectionFilter, TagFilter, StatusFilter, ImportanceFilter, ReviewFilter, ShowTrash))) Captures.Add(capture);
        ReplaceSelection(Captures.Where(capture => selectedIds.Contains(capture.Id)));
        RefreshMarkdown();
        OnPropertyChanged(nameof(IncludedCount));
    }

    private void RefreshMarkdown()
    {
        if (_activeSession?.Invoke() is { } session)
            PreviewMarkdown = MarkdownComposition.ComposeReview(session, _library.Captures);
    }
    private static string ResultKey(Guid sessionId) => $"review.result-markdown:{sessionId:N}";
}
public sealed class InstructionsViewModel : ViewModelBase;
public sealed class DocumentViewModel : ViewModelBase
{
    private SemanticDocumentPreview _preview;
    private ExportScope _scope;
    private PreviewItem? _selectedItem;

    public DocumentViewModel(SemanticDocument? document = null,
        IReadOnlyDictionary<string, PreviewContentState>? states = null)
    {
        _preview = new(document ?? new SemanticDocument("Documento", []), states);
        Items = new(_preview.Items);
        IncludeCommand = new RelayCommand(value => SetIncluded(value, true));
        ExcludeCommand = new RelayCommand(value => SetIncluded(value, false));
        MoveUpCommand = new RelayCommand(value => Move(value, -1));
        MoveDownCommand = new RelayCommand(value => Move(value, 1));
        ExportCommand = new AsyncRelayCommand(async (_, _) => await (ExportRequested?.Invoke(CreateExportDocument(), Scope) ?? Task.CompletedTask));
    }

    public ObservableCollection<PreviewItem> Items { get; }
    public PreviewItem? SelectedItem { get => _selectedItem; set { _selectedItem = value; OnPropertyChanged(); } }
    public ExportScope Scope { get => _scope; set { _scope = value; OnPropertyChanged(); } }
    public IReadOnlyList<ExportScope> Scopes { get; } = Enum.GetValues<ExportScope>();
    public bool HasWarnings => _preview.HasWarnings;
    public ICommand IncludeCommand { get; }
    public ICommand ExcludeCommand { get; }
    public ICommand MoveUpCommand { get; }
    public ICommand MoveDownCommand { get; }
    public ICommand ExportCommand { get; }
    public event Func<SemanticDocument, ExportScope, Task>? ExportRequested;

    public SemanticDocument CreateExportDocument() => _preview.CreateDocument(Scope,
        SelectedItem?.Node.Type == SemanticNodeType.Section ? SelectedItem.StableKey : SelectedItem?.SectionKey);

    public void ReplaceDocument(SemanticDocument document)
    {
        var excluded = Items.Where(x => !x.IsIncluded).Select(x => x.StableKey).ToHashSet();
        var editedContent = Items.Where(x => x.IsContentEdited)
            .ToDictionary(x => x.StableKey, x => x.Content);
        var states = Items.ToDictionary(x => x.StableKey, x => x.State);
        var selectedKey = SelectedItem?.StableKey;
        _preview = new(document, states);
        _preview.SetIncluded(excluded, false);
        foreach (var item in _preview.Items)
            if (editedContent.TryGetValue(item.StableKey, out var content)) item.Content = content;
        Refresh();
        SelectedItem = Items.FirstOrDefault(x => x.StableKey == selectedKey);
    }

    private void SetIncluded(object? value, bool included)
    {
        var item = value as PreviewItem ?? SelectedItem;
        if (item is null) return;
        _preview.SetIncluded([item.StableKey], included);
        Refresh();
    }

    private void Move(object? value, int offset)
    {
        var item = value as PreviewItem ?? SelectedItem;
        if (item is null || item.Node.Type == SemanticNodeType.Section) return;
        _preview.Move(item.StableKey, Math.Max(0, item.Order - 1 + offset));
        Refresh();
    }

    private void Refresh()
    {
        Items.Clear();
        foreach (var item in _preview.Items.OrderBy(item => item.SectionKey).ThenBy(item => item.Order)) Items.Add(item);
        OnPropertyChanged(nameof(HasWarnings));
    }
}
public sealed class SettingsViewModel : ViewModelBase
{
    internal const string SettingsDocumentKey = "hierarchical-settings";
    private const string ImageTransmissionConsentKey = "privacy.image-upload-consent";
    private readonly IGlobalHotkeyService? _hotkeys;
    private readonly EffectiveSettingsResolver _settingsResolver = new();
    private readonly Dictionary<SettingsLevel, SettingsValues> _layers = [];
    private string _conflictMessage = string.Empty;
    private SettingsLevel _selectedLevel = SettingsLevel.Global;
    private readonly IApiCredentialStore? _credentials;
    private readonly ISettingsRepository? _repository;
    private readonly IUnitOfWork? _unitOfWork;
    private SettingsDocument _document = new();
    private Guid? _sessionId;
    private Guid? _sectionId;
    private Guid? _screenshotId;
    private bool _imageUploadConsent;

    public SettingsViewModel()
    {
        SaveCommand = new RelayCommand(_ => { });
        InitializeHierarchicalSettings();
    }

    public SettingsViewModel(IGlobalHotkeyService hotkeys, IApiCredentialStore? credentials = null)
        : this(hotkeys, credentials, null, null) { }

    public SettingsViewModel(IGlobalHotkeyService hotkeys, IApiCredentialStore? credentials,
        ISettingsRepository? repository, IUnitOfWork? unitOfWork)
    {
        _hotkeys = hotkeys;
        _credentials = credentials;
        _repository = repository;
        _unitOfWork = unitOfWork;
        Bindings = new(DefaultBindings().Select(binding => new HotkeyBindingEditorViewModel(binding)));
        SaveCommand = new RelayCommand(_ => Save());
        SaveCredentialCommand = new AsyncRelayCommand(async (value, cancellationToken) => await SaveCredentialAsync(value));
        VerifyCredentialCommand = new AsyncRelayCommand(async (value, cancellationToken) => await VerifyCredentialAsync(value));
        DeleteCredentialCommand = new AsyncRelayCommand(async (value, cancellationToken) => await DeleteCredentialAsync(value));
        InitializeHierarchicalSettings();
    }

    public ObservableCollection<HotkeyBindingEditorViewModel> Bindings { get; } = [];
    public string ConflictMessage { get => _conflictMessage; private set { _conflictMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasConflict)); } }
    public bool HasConflict => !string.IsNullOrEmpty(ConflictMessage);
    public ICommand SaveCommand { get; }
    public ObservableCollection<ApiCredentialEditorViewModel> CredentialProfiles { get; } =
        new(Enum.GetValues<ApiCredentialProfile>().Select(profile => new ApiCredentialEditorViewModel(profile)));
    public ICommand SaveCredentialCommand { get; } = new RelayCommand(_ => { });
    public ICommand VerifyCredentialCommand { get; } = new RelayCommand(_ => { });
    public ICommand DeleteCredentialCommand { get; } = new RelayCommand(_ => { });
    public IReadOnlyList<SettingsLevel> Levels { get; } = Enum.GetValues<SettingsLevel>();
    public ObservableCollection<EffectiveSettingEditorViewModel> EffectiveValues { get; } = [];
    public SettingsLevel SelectedLevel
    {
        get => _selectedLevel;
        set { _selectedLevel = value; OnPropertyChanged(); RefreshEffectiveValues(); }
    }
    public ICommand OverrideSettingCommand { get; private set; } = null!;
    public ICommand RestoreSettingCommand { get; private set; } = null!;
    public bool ImageUploadConsent
    {
        get => _imageUploadConsent;
        set { if (_imageUploadConsent == value) return; _imageUploadConsent = value; OnPropertyChanged(); _ = PersistConsentAsync(); }
    }

    public void ReplaceBinding(HotkeyAction action, HotkeyGesture gesture)
    {
        var current = Bindings.First(binding => binding.Action == action);
        var index = Bindings.IndexOf(current);
        current.Modifiers = gesture.Modifiers;
        current.VirtualKey = gesture.VirtualKey;
    }

    private void Save()
    {
        var result = _hotkeys!.Apply(Bindings.Select(binding => binding.ToBinding()).ToArray());
        ConflictMessage = result.Succeeded
            ? string.Empty
            : string.Join(Environment.NewLine, result.Conflicts.Select(conflict => conflict.Message).Distinct());
    }

    public async Task LoadCredentialsAsync()
    {
        if (_credentials is null) return;
        foreach (var editor in CredentialProfiles) editor.MaskedValue = await _credentials.GetMaskedAsync(editor.Profile);
    }

    /// <summary>Loads the four independently persisted scopes and selects the entities being edited.</summary>
    public async Task LoadSettingsAsync(Guid? sessionId = null, Guid? sectionId = null, Guid? screenshotId = null,
        CancellationToken cancellationToken = default)
    {
        _sessionId = sessionId; _sectionId = sectionId; _screenshotId = screenshotId;
        _document = _repository is null
            ? new SettingsDocument()
            : await _repository.GetAsync<SettingsDocument>(SettingsDocumentKey, cancellationToken) ?? new SettingsDocument();
        LoadLayersFromDocument();
        _imageUploadConsent = _repository is not null && await _repository.GetAsync<bool?>(ImageTransmissionConsentKey, cancellationToken) == true;
        OnPropertyChanged(nameof(ImageUploadConsent));
        RefreshEffectiveValues();
    }

    public EffectiveSettings ResolveEffectiveSettings() => _settingsResolver.Resolve(new(
        _layers[SettingsLevel.ApplicationDefaults], _layers[SettingsLevel.Global],
        _layers[SettingsLevel.Session], _layers[SettingsLevel.Section],
        _layers[SettingsLevel.ScreenshotOverride]));

    private async Task SaveCredentialAsync(object? value)
    {
        if (_credentials is null || value is not ApiCredentialEditorViewModel editor || string.IsNullOrWhiteSpace(editor.PendingValue)) return;
        await _credentials.SaveAsync(editor.Profile, editor.PendingValue);
        editor.PendingValue = string.Empty;
        editor.MaskedValue = await _credentials.GetMaskedAsync(editor.Profile);
        editor.Status = "Credencial guardada";
    }

    private async Task VerifyCredentialAsync(object? value)
    {
        if (_credentials is null || value is not ApiCredentialEditorViewModel editor || string.IsNullOrEmpty(editor.PendingValue)) return;
        editor.Status = await _credentials.VerifyAsync(editor.Profile, editor.PendingValue) ? "Credencial válida" : "No coincide";
        editor.PendingValue = string.Empty;
    }

    private async Task DeleteCredentialAsync(object? value)
    {
        if (_credentials is null || value is not ApiCredentialEditorViewModel editor) return;
        await _credentials.DeleteAsync(editor.Profile);
        editor.PendingValue = string.Empty;
        editor.MaskedValue = null;
        editor.Status = "Credencial eliminada";
    }

    private void InitializeHierarchicalSettings()
    {
        _layers[SettingsLevel.ApplicationDefaults] = new()
        {
            Language = "Español",
            Provider = "OpenAI",
            Model = "gpt-4.1-mini",
            PromptTemplate = "Genera apuntes claros y estructurados.",
            IncludeImages = true,
            MaximumImageSide = 2560
        };
        _layers[SettingsLevel.Global] = new();
        _layers[SettingsLevel.Session] = new();
        _layers[SettingsLevel.Section] = new();
        _layers[SettingsLevel.ScreenshotOverride] = new();
        OverrideSettingCommand = new RelayCommand(value => { if (value is EffectiveSettingEditorViewModel row) Override(row); });
        RestoreSettingCommand = new RelayCommand(value => { if (value is EffectiveSettingEditorViewModel row) Restore(row); });
        RefreshEffectiveValues();
    }

    private void LoadLayersFromDocument()
    {
        _layers[SettingsLevel.Global] = _document.Global;
        _layers[SettingsLevel.Session] = _sessionId is { } session && _document.Sessions.TryGetValue(session, out var sv) ? sv : new();
        _layers[SettingsLevel.Section] = _sectionId is { } section && _document.Sections.TryGetValue(section, out var secv) ? secv : new();
        _layers[SettingsLevel.ScreenshotOverride] = _screenshotId is { } screenshot && _document.ScreenshotOverrides.TryGetValue(screenshot, out var cv) ? cv : new();
    }

    private async Task PersistSelectedLayerAsync()
    {
        if (_repository is null || SelectedLevel == SettingsLevel.ApplicationDefaults) return;
        var sessions = new Dictionary<Guid, SettingsValues>(_document.Sessions);
        var sections = new Dictionary<Guid, SettingsValues>(_document.Sections);
        var screenshots = new Dictionary<Guid, SettingsValues>(_document.ScreenshotOverrides);
        var global = _document.Global;
        switch (SelectedLevel)
        {
            case SettingsLevel.Global: global = _layers[SelectedLevel]; break;
            case SettingsLevel.Session when _sessionId is { } id: sessions[id] = _layers[SelectedLevel]; break;
            case SettingsLevel.Section when _sectionId is { } id: sections[id] = _layers[SelectedLevel]; break;
            case SettingsLevel.ScreenshotOverride when _screenshotId is { } id: screenshots[id] = _layers[SelectedLevel]; break;
            default: return;
        }
        _document = _document with { Global = global, Sessions = sessions, Sections = sections, ScreenshotOverrides = screenshots };
        await _repository.SetAsync(SettingsDocumentKey, _document);
        if (_unitOfWork is not null) await _unitOfWork.SaveChangesAsync();
    }

    private async Task PersistConsentAsync()
    {
        if (_repository is null) return;
        await _repository.SetAsync(ImageTransmissionConsentKey, ImageUploadConsent);
        if (_unitOfWork is not null) await _unitOfWork.SaveChangesAsync();
    }

    private void Override(EffectiveSettingEditorViewModel row)
    {
        if (SelectedLevel == SettingsLevel.ApplicationDefaults) return;
        var values = _layers[SelectedLevel];
        _layers[SelectedLevel] = row.Key switch
        {
            nameof(SettingsValues.Language) => values with { Language = row.EditValue },
            nameof(SettingsValues.Provider) => values with { Provider = row.EditValue },
            nameof(SettingsValues.Model) => values with { Model = row.EditValue },
            nameof(SettingsValues.PromptTemplate) => values with { PromptTemplate = row.EditValue },
            nameof(SettingsValues.IncludeImages) when bool.TryParse(row.EditValue, out var parsed) => values with { IncludeImages = parsed },
            nameof(SettingsValues.MaximumImageSide) when int.TryParse(row.EditValue, out var parsed) && parsed > 0 => values with { MaximumImageSide = parsed },
            _ => values
        };
        RefreshEffectiveValues();
        _ = PersistSelectedLayerAsync();
    }

    private void Restore(EffectiveSettingEditorViewModel row)
    {
        if (SelectedLevel == SettingsLevel.ApplicationDefaults) return;
        var values = _layers[SelectedLevel];
        _layers[SelectedLevel] = row.Key switch
        {
            nameof(SettingsValues.Language) => values with { Language = null },
            nameof(SettingsValues.Provider) => values with { Provider = null },
            nameof(SettingsValues.Model) => values with { Model = null },
            nameof(SettingsValues.PromptTemplate) => values with { PromptTemplate = null },
            nameof(SettingsValues.IncludeImages) => values with { IncludeImages = null },
            nameof(SettingsValues.MaximumImageSide) => values with { MaximumImageSide = null },
            _ => values
        };
        RefreshEffectiveValues();
        _ = PersistSelectedLayerAsync();
    }

    private void RefreshEffectiveValues()
    {
        var effective = _settingsResolver.Resolve(new(
            _layers[SettingsLevel.ApplicationDefaults], _layers[SettingsLevel.Global],
            _layers[SettingsLevel.Session], _layers[SettingsLevel.Section],
            _layers[SettingsLevel.ScreenshotOverride]));
        EffectiveValues.Clear();
        Add(nameof(SettingsValues.Language), "Idioma", effective.Language.Value, effective.Language.Provenance);
        Add(nameof(SettingsValues.Provider), "Proveedor", effective.Provider.Value, effective.Provider.Provenance);
        Add(nameof(SettingsValues.Model), "Modelo", effective.Model.Value, effective.Model.Provenance);
        Add(nameof(SettingsValues.PromptTemplate), "Plantilla", effective.PromptTemplate.Value, effective.PromptTemplate.Provenance);
        Add(nameof(SettingsValues.IncludeImages), "Incluir imágenes", effective.IncludeImages.Value, effective.IncludeImages.Provenance);
        Add(nameof(SettingsValues.MaximumImageSide), "Tamaño máximo", effective.MaximumImageSide.Value, effective.MaximumImageSide.Provenance);
    }

    private void Add(string key, string label, object value, string provenance) =>
        EffectiveValues.Add(new(key, label, value.ToString() ?? string.Empty, provenance));

    public static IReadOnlyList<HotkeyBinding> DefaultBindings() =>
    [
        New(HotkeyAction.CaptureFullDesktop, '1'), New(HotkeyAction.CaptureCurrentMonitor, '2'),
        New(HotkeyAction.CaptureActiveWindow, '3'), New(HotkeyAction.CaptureRegion, '4'),
        New(HotkeyAction.TogglePause, 'P'), New(HotkeyAction.NextSection, 0x22),
        New(HotkeyAction.PreviousSection, 0x21), New(HotkeyAction.Undo, 'Z'),
        New(HotkeyAction.MarkImportant, 'I'), New(HotkeyAction.AddContext, 'K')
    ];

    private static HotkeyBinding New(HotkeyAction action, char key) => New(action, (uint)key);
    private static HotkeyBinding New(HotkeyAction action, uint key) => new(action, new(HotkeyModifiers.Control | HotkeyModifiers.Shift, key));
}

public sealed class ApiCredentialEditorViewModel(ApiCredentialProfile profile) : ViewModelBase
{
    private string _pendingValue = string.Empty;
    private string? _maskedValue;
    private string _status = string.Empty;
    public ApiCredentialProfile Profile { get; } = profile;
    public string DisplayName => Profile == ApiCredentialProfile.Extraction ? "Extracción" : "Composición";
    public string PendingValue { get => _pendingValue; set { _pendingValue = value; OnPropertyChanged(); } }
    public string? MaskedValue { get => _maskedValue; set { _maskedValue = value; OnPropertyChanged(); } }
    public string Status { get => _status; set { _status = value; OnPropertyChanged(); } }
}

public sealed class EffectiveSettingEditorViewModel(string key, string label, string value, string provenance) : ViewModelBase
{
    private string _editValue = value;
    public string Key { get; } = key;
    public string Label { get; } = label;
    public string EffectiveValue { get; } = value;
    public string Provenance { get; } = provenance;
    public string EditValue { get => _editValue; set { _editValue = value; OnPropertyChanged(); } }
}

public sealed class HotkeyBindingEditorViewModel(HotkeyBinding binding) : ViewModelBase
{
    private HotkeyModifiers _modifiers = binding.Gesture.Modifiers;
    private uint _virtualKey = binding.Gesture.VirtualKey;
    public HotkeyAction Action { get; } = binding.Action;
    public HotkeyModifiers Modifiers { get => _modifiers; set { _modifiers = value; OnPropertyChanged(); OnPropertyChanged(nameof(Gesture)); } }
    public uint VirtualKey { get => _virtualKey; set { _virtualKey = value; OnPropertyChanged(); OnPropertyChanged(nameof(Gesture)); } }
    public bool IsEnabled { get; set; } = binding.IsEnabled;
    public HotkeyGesture Gesture => new(Modifiers, VirtualKey);
    public HotkeyBinding ToBinding() => new(Action, Gesture, IsEnabled);
}
