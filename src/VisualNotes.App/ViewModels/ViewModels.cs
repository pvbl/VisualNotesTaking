using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

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
    private ViewModelBase _currentViewModel = new SessionViewModel();
    private bool _isPaused;

    public ViewModelBase CurrentViewModel { get => _currentViewModel; private set { _currentViewModel = value; OnPropertyChanged(); } }
    public bool IsPaused { get => _isPaused; private set { _isPaused = value; OnPropertyChanged(); OnPropertyChanged(nameof(SessionStatus)); } }
    public string SessionStatus => IsPaused ? "Sesión pausada" : "Sesión activa";
    public ICommand NavigateCommand { get; }
    public ICommand TogglePauseCommand { get; }

    public MainViewModel()
    {
        NavigateCommand = new RelayCommand(page => CurrentViewModel = page switch
        {
            "Captures" => new CapturesViewModel(), "Instructions" => new InstructionsViewModel(),
            "Document" => new DocumentViewModel(), "Settings" => new SettingsViewModel(), _ => new SessionViewModel()
        });
        TogglePauseCommand = new RelayCommand(_ => IsPaused = !IsPaused);
    }
}

public sealed class SessionViewModel : ViewModelBase;
public sealed class CapturesViewModel : ViewModelBase;
public sealed class InstructionsViewModel : ViewModelBase;
public sealed class DocumentViewModel : ViewModelBase;
public sealed class SettingsViewModel : ViewModelBase;
