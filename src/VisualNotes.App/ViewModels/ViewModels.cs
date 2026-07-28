using System.Collections.ObjectModel;
using System.ComponentModel;
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

public sealed class MainViewModel : ViewModelBase
{
    private readonly SessionCoordinator _coordinator;
    private readonly ISessionRepository _repository;
    private ViewModelBase _currentViewModel;
    private NoteSession? _activeSession;

    private readonly SettingsViewModel? _settings;

    public MainViewModel(SessionCoordinator coordinator, ISessionRepository repository, IGlobalHotkeyService? hotkeys = null, IApiCredentialStore? credentials = null)
    {
        _coordinator = coordinator; _repository = repository;
        _settings = hotkeys is null ? null : new SettingsViewModel(hotkeys, credentials);
        Sessions = new SessionViewModel(coordinator, repository, Activate);
        _currentViewModel = Sessions;
        NavigateCommand = new RelayCommand(page => CurrentViewModel = page switch
        {
            "Captures" => new CapturesViewModel(), "Instructions" => new InstructionsViewModel(),
            "Document" => new DocumentViewModel(), "Settings" => _settings ?? new SettingsViewModel(), _ => Sessions
        });
        TogglePauseCommand = new RelayCommand(async _ =>
        {
            if (ActiveSession is null) return;
            await _coordinator.SetPausedAsync(ActiveSession, !ActiveSession.IsPaused);
            RefreshHeader();
        });
        CaptureRegionCommand = new RelayCommand(_ => CaptureRegionRequested?.Invoke());
        RedefineRegionCommand = new RelayCommand(_ => RedefineRegionRequested?.Invoke());
    }

    public SessionViewModel Sessions { get; }
    public ViewModelBase CurrentViewModel { get => _currentViewModel; private set { _currentViewModel = value; OnPropertyChanged(); } }
    public NoteSession? ActiveSession { get => _activeSession; private set { _activeSession = value; OnPropertyChanged(); RefreshHeader(); } }
    public string ActiveSessionName => ActiveSession?.Name ?? "Sin sesión activa";
    public string ActiveSectionName => ActiveSession?.Sections.FirstOrDefault(x => x.Id == ActiveSession.ActiveSectionId)?.Title ?? "Sin sección";
    public string SessionStatus => ActiveSession is null ? "Crea o continúa una sesión" : ActiveSession.IsPaused ? "Sesión pausada" : "Sesión activa";
    public ICommand NavigateCommand { get; }
    public ICommand TogglePauseCommand { get; }
    public ICommand CaptureRegionCommand { get; }
    public ICommand RedefineRegionCommand { get; }
    public event Action? CaptureRegionRequested;
    public event Action? RedefineRegionRequested;
    public Action? UndoRequested { get; set; }
    public Action? MarkImportantRequested { get; set; }
    public Action? AddContextRequested { get; set; }

    public async Task InitializeAsync()
    {
        if (_settings is not null) await _settings.LoadCredentialsAsync();
        await Sessions.LoadAsync();
        var restored = await _coordinator.RestoreLastOpenAsync();
        if (restored is not null) Activate(restored);
    }

    private void Activate(NoteSession session) => ActiveSession = session;
    public void RefreshHeader() { OnPropertyChanged(nameof(ActiveSessionName)); OnPropertyChanged(nameof(ActiveSectionName)); OnPropertyChanged(nameof(SessionStatus)); }
}

public sealed class SessionViewModel : ViewModelBase
{
    private readonly SessionCoordinator _coordinator;
    private readonly ISessionRepository _repository;
    private readonly Action<NoteSession> _activate;
    private NoteSession _draft = NewDraft();
    private NoteSession? _selected;
    private NoteSection? _selectedSection;

    public SessionViewModel(SessionCoordinator coordinator, ISessionRepository repository, Action<NoteSession> activate)
    {
        _coordinator = coordinator; _repository = repository; _activate = activate;
        CreateCommand = new RelayCommand(async _ => { var session = await _coordinator.CreateAsync(Draft); RecentSessions.Insert(0, session); SelectedSession = session; Draft = NewDraft(); });
        DuplicateCommand = new RelayCommand(async _ => { if (SelectedSession is null) return; var copy = await _coordinator.CreateAsync(NewDraft(), SelectedSession.Id); RecentSessions.Insert(0, copy); SelectedSession = copy; });
        ContinueCommand = new RelayCommand(async _ => { if (SelectedSession is null) return; await _coordinator.ContinueAsync(SelectedSession); _activate(SelectedSession); });
        SaveCommand = new RelayCommand(async _ => { if (SelectedSession is not null) await _coordinator.SetPausedAsync(SelectedSession, SelectedSession.IsPaused); });
        AddSectionCommand = new RelayCommand(async _ => { if (SelectedSession is null) return; SelectedSection = await _coordinator.AddSectionAsync(SelectedSession, "Nueva sección", parentId: SelectedSection?.Id); _activate(SelectedSession); });
        ActivateSectionCommand = new RelayCommand(async value => { if (SelectedSession is null || value is not NoteSection section) return; await _coordinator.ActivateSectionAsync(SelectedSession, section.Id); SelectedSection = section; _activate(SelectedSession); });
        RenameSectionCommand = new RelayCommand(async _ => { if (SelectedSection is not null) await _coordinator.RenameSectionAsync(SelectedSection, SelectedSection.Title); });
        MoveUpCommand = new RelayCommand(async _ => { if (SelectedSession is null || SelectedSection is null) return; await _coordinator.ReorderSectionAsync(SelectedSession, SelectedSection.Id, SelectedSection.Order - 1); OnPropertyChanged(nameof(OrderedSections)); });
        MoveDownCommand = new RelayCommand(async _ => { if (SelectedSession is null || SelectedSection is null) return; await _coordinator.ReorderSectionAsync(SelectedSession, SelectedSection.Id, SelectedSection.Order + 1); OnPropertyChanged(nameof(OrderedSections)); });
    }

    public ObservableCollection<NoteSession> RecentSessions { get; } = [];
    public NoteSession Draft { get => _draft; set { _draft = value; OnPropertyChanged(); } }
    public NoteSession? SelectedSession { get => _selected; set { _selected = value; OnPropertyChanged(); OnPropertyChanged(nameof(OrderedSections)); if (value is not null) _activate(value); } }
    public NoteSection? SelectedSection { get => _selectedSection; set { _selectedSection = value; OnPropertyChanged(); } }
    public IEnumerable<NoteSection> OrderedSections => SelectedSession?.Sections.OrderBy(x => x.Order) ?? [];
    public ICommand CreateCommand { get; } public ICommand DuplicateCommand { get; } public ICommand ContinueCommand { get; }
    public ICommand SaveCommand { get; } public ICommand AddSectionCommand { get; } public ICommand ActivateSectionCommand { get; }
    public ICommand RenameSectionCommand { get; } public ICommand MoveUpCommand { get; } public ICommand MoveDownCommand { get; }

    public async Task LoadAsync() { RecentSessions.Clear(); foreach (var session in await _repository.ListAsync()) RecentSessions.Add(session); }
    private static NoteSession NewDraft() => new() { WorkingFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), PlannedDocumentName = "Apuntes.md" };
}

public sealed class CapturesViewModel : ViewModelBase
{
    private readonly CaptureLibrary _library;
    private Screenshot? _selectedCapture;
    private bool _isQuickContextOpen;
    private Guid? _sectionFilter;
    private Guid? _targetSectionId;
    private string _tagFilter = string.Empty;
    private ScreenshotStatus? _statusFilter;
    private CaptureImportance? _importanceFilter;
    private ReviewFilter _reviewFilter;

    public CapturesViewModel(IEnumerable<Screenshot>? captures = null)
    {
        _library = new CaptureLibrary(captures ?? []);
        Sections = _library.Captures.Where(capture => capture.Section is not null).Select(capture => capture.Section!).DistinctBy(section => section.Id).OrderBy(section => section.Order).ToArray();
        SelectedCaptures.CollectionChanged += (_, _) => OnPropertyChanged(nameof(SelectionCount));
        ApplyChipCommand = new RelayCommand(value => { if (SelectedCapture is null || value is not string chip) return; SelectedCapture.Tags = string.Join(", ", SelectedCapture.Tags.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Append(chip).Distinct(StringComparer.OrdinalIgnoreCase)); SelectedCapture.CaptureInstruction = CaptureInstructionResolver.Resolve([chip], SelectedCapture.CaptureInstruction); OnPropertyChanged(nameof(SelectedCapture)); Refresh(); });
        CloseQuickContextCommand = new RelayCommand(_ => IsQuickContextOpen = false);
        ReanalyzeCommand = new RelayCommand(_ => Run(_library.Reprocess));
        RegenerateNoteCommand = new RelayCommand(_ => Run(ids => { _library.Reprocess(ids); foreach (var capture in Selected()) capture.ProcessingStatus = ScreenshotStatus.NeedsReview; }));
        ExcludeCommand = new RelayCommand(_ => Run(_library.Exclude));
        DeleteCommand = new RelayCommand(_ => Run(_library.Delete));
        RestoreCommand = new RelayCommand(_ => Run(_library.Restore));
        UndoCommand = new RelayCommand(_ => { if (_library.Undo()) Refresh(); });
        MoveToSectionCommand = new RelayCommand(value => { if (value is Guid id) Run(ids => _library.MoveToSection(ids, id)); });
        ReorderCommand = new RelayCommand(value => { if (value is int index) Run(ids => _library.Reorder(ids, index)); });
        ClearFiltersCommand = new RelayCommand(_ => { SectionFilter = null; TagFilter = string.Empty; StatusFilter = null; ImportanceFilter = null; ReviewFilter = VisualNotes.Core.Services.ReviewFilter.All; });
        Refresh();
    }

    public ObservableCollection<Screenshot> Captures { get; } = [];
    public ObservableCollection<Screenshot> SelectedCaptures { get; } = [];
    public IReadOnlyCollection<string> QuickChips => CaptureInstructionResolver.QuickChips;
    public IReadOnlyList<NoteSection> Sections { get; }
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
    public ICommand ApplyChipCommand { get; }
    public ICommand CloseQuickContextCommand { get; }
    public ICommand ReanalyzeCommand { get; }
    public ICommand RegenerateNoteCommand { get; }
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

    private IReadOnlyCollection<Guid> SelectedIds() => Selected().Select(capture => capture.Id).ToArray();
    private IEnumerable<Screenshot> Selected() => SelectedCaptures.Count == 0 && SelectedCapture is not null ? [SelectedCapture] : SelectedCaptures;
    private void Run(Action<IReadOnlyCollection<Guid>> action) { action(SelectedIds()); Refresh(); }
    private void Refresh()
    {
        var selectedIds = SelectedCaptures.Select(capture => capture.Id).ToHashSet();
        Captures.Clear();
        foreach (var capture in _library.Query(new(SectionFilter, TagFilter, StatusFilter, ImportanceFilter, ReviewFilter))) Captures.Add(capture);
        ReplaceSelection(Captures.Where(capture => selectedIds.Contains(capture.Id)));
    }
}
public sealed class InstructionsViewModel : ViewModelBase;
public sealed class DocumentViewModel : ViewModelBase
{
    private readonly SemanticDocumentPreview _preview;
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
        ExportCommand = new RelayCommand(_ => ExportRequested?.Invoke(CreateExportDocument(), Scope));
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
    public event Action<SemanticDocument, ExportScope>? ExportRequested;

    public SemanticDocument CreateExportDocument() => _preview.CreateDocument(Scope,
        SelectedItem?.Node.Type == SemanticNodeType.Section ? SelectedItem.StableKey : SelectedItem?.SectionKey);

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
    private readonly IGlobalHotkeyService? _hotkeys;
    private readonly EffectiveSettingsResolver _settingsResolver = new();
    private readonly Dictionary<SettingsLevel, SettingsValues> _layers = [];
    private string _conflictMessage = string.Empty;
    private SettingsLevel _selectedLevel = SettingsLevel.Global;
    private readonly IApiCredentialStore? _credentials;

    public SettingsViewModel()
    {
        SaveCommand = new RelayCommand(_ => { });
        InitializeHierarchicalSettings();
    }

    public SettingsViewModel(IGlobalHotkeyService hotkeys, IApiCredentialStore? credentials = null)
    {
        _hotkeys = hotkeys;
        _credentials = credentials;
        Bindings = new(DefaultBindings().Select(binding => new HotkeyBindingEditor(binding)));
        SaveCommand = new RelayCommand(_ => Save());
        SaveCredentialCommand = new RelayCommand(async value => await SaveCredentialAsync(value));
        VerifyCredentialCommand = new RelayCommand(async value => await VerifyCredentialAsync(value));
        DeleteCredentialCommand = new RelayCommand(async value => await DeleteCredentialAsync(value));
        InitializeHierarchicalSettings();
    }

    public ObservableCollection<HotkeyBindingEditor> Bindings { get; } = [];
    public string ConflictMessage { get => _conflictMessage; private set { _conflictMessage = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasConflict)); } }
    public bool HasConflict => !string.IsNullOrEmpty(ConflictMessage);
    public ICommand SaveCommand { get; }
    public ObservableCollection<ApiCredentialEditor> CredentialProfiles { get; } =
        new(Enum.GetValues<ApiCredentialProfile>().Select(profile => new ApiCredentialEditor(profile)));
    public ICommand SaveCredentialCommand { get; } = new RelayCommand(_ => { });
    public ICommand VerifyCredentialCommand { get; } = new RelayCommand(_ => { });
    public ICommand DeleteCredentialCommand { get; } = new RelayCommand(_ => { });
    public IReadOnlyList<SettingsLevel> Levels { get; } = Enum.GetValues<SettingsLevel>();
    public ObservableCollection<EffectiveSettingEditor> EffectiveValues { get; } = [];
    public SettingsLevel SelectedLevel
    {
        get => _selectedLevel;
        set { _selectedLevel = value; OnPropertyChanged(); RefreshEffectiveValues(); }
    }
    public ICommand OverrideSettingCommand { get; private set; } = null!;
    public ICommand RestoreSettingCommand { get; private set; } = null!;

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

    private async Task SaveCredentialAsync(object? value)
    {
        if (_credentials is null || value is not ApiCredentialEditor editor || string.IsNullOrWhiteSpace(editor.PendingValue)) return;
        await _credentials.SaveAsync(editor.Profile, editor.PendingValue);
        editor.PendingValue = string.Empty;
        editor.MaskedValue = await _credentials.GetMaskedAsync(editor.Profile);
        editor.Status = "Credencial guardada";
    }

    private async Task VerifyCredentialAsync(object? value)
    {
        if (_credentials is null || value is not ApiCredentialEditor editor || string.IsNullOrEmpty(editor.PendingValue)) return;
        editor.Status = await _credentials.VerifyAsync(editor.Profile, editor.PendingValue) ? "Credencial válida" : "No coincide";
        editor.PendingValue = string.Empty;
    }

    private async Task DeleteCredentialAsync(object? value)
    {
        if (_credentials is null || value is not ApiCredentialEditor editor) return;
        await _credentials.DeleteAsync(editor.Profile);
        editor.PendingValue = string.Empty;
        editor.MaskedValue = null;
        editor.Status = "Credencial eliminada";
    }

    private void InitializeHierarchicalSettings()
    {
        _layers[SettingsLevel.ApplicationDefaults] = new()
        {
            Language = "Español", Provider = "OpenAI", Model = "gpt-4.1-mini",
            PromptTemplate = "Genera apuntes claros y estructurados.", IncludeImages = true,
            MaximumImageSide = 2560
        };
        _layers[SettingsLevel.Global] = new();
        _layers[SettingsLevel.Session] = new();
        _layers[SettingsLevel.Section] = new();
        _layers[SettingsLevel.ScreenshotOverride] = new();
        OverrideSettingCommand = new RelayCommand(value => { if (value is EffectiveSettingEditor row) Override(row); });
        RestoreSettingCommand = new RelayCommand(value => { if (value is EffectiveSettingEditor row) Restore(row); });
        RefreshEffectiveValues();
    }

    private void Override(EffectiveSettingEditor row)
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
    }

    private void Restore(EffectiveSettingEditor row)
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

public sealed class ApiCredentialEditor(ApiCredentialProfile profile) : ViewModelBase
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

public sealed class EffectiveSettingEditor(string key, string label, string value, string provenance) : ViewModelBase
{
    private string _editValue = value;
    public string Key { get; } = key;
    public string Label { get; } = label;
    public string EffectiveValue { get; } = value;
    public string Provenance { get; } = provenance;
    public string EditValue { get => _editValue; set { _editValue = value; OnPropertyChanged(); } }
}

public sealed class HotkeyBindingEditor(HotkeyBinding binding) : ViewModelBase
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
