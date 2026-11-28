using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NotepadAvalonia.Models;
using System.Threading.Tasks;

namespace NotepadAvalonia.ViewModels;

public partial class FindReplaceViewModel : ObservableObject
{
    private readonly MainWindowViewModel _owner;

    public FindReplaceViewModel(MainWindowViewModel owner, bool findMode)
    {
        _owner = owner;
        IsFindMode = findMode;
    }

    public bool IsFindMode { get; }
    public bool IsReplaceMode => !IsFindMode;
    public string Title => IsFindMode ? "Find" : "Replace";

    [ObservableProperty]
    private string? _searchFeedback;

    public string FindText
    {
        get => _owner.SearchSettings.SearchString;
        set
        {
            if (_owner.SearchSettings.SearchString != value)
            {
                _owner.SearchSettings.SearchString = value ?? string.Empty;
                _owner.RefreshSearchPreview();
                OnPropertyChanged();
                FindNextCommand.NotifyCanExecuteChanged();
                FindPreviousCommand.NotifyCanExecuteChanged();
                ReplaceCommand.NotifyCanExecuteChanged();
                ReplaceAllCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string ReplaceText
    {
        get => _owner.SearchSettings.ReplaceString;
        set
        {
            if (_owner.SearchSettings.ReplaceString != value)
            {
                _owner.SearchSettings.ReplaceString = value ?? string.Empty;
                OnPropertyChanged();
            }
        }
    }

    public bool MatchCase
    {
        get => _owner.SearchSettings.MatchCase;
        set
        {
            if (_owner.SearchSettings.MatchCase != value)
            {
                _owner.SearchSettings.MatchCase = value;
                _owner.RefreshSearchPreview();
                OnPropertyChanged();
            }
        }
    }

    public bool WrapAround
    {
        get => _owner.SearchSettings.WrapAround;
        set
        {
            if (_owner.SearchSettings.WrapAround != value)
            {
                _owner.SearchSettings.WrapAround = value;
                OnPropertyChanged();
            }
        }
    }

    public bool UseRegex
    {
        get => _owner.SearchSettings.UseRegex;
        set
        {
            if (_owner.SearchSettings.UseRegex != value)
            {
                _owner.SearchSettings.UseRegex = value;
                _owner.RefreshSearchPreview();
                OnPropertyChanged();
            }
        }
    }

    public bool WholeWord
    {
        get => _owner.SearchSettings.WholeWord;
        set
        {
            if (_owner.SearchSettings.WholeWord != value)
            {
                _owner.SearchSettings.WholeWord = value;
                _owner.RefreshSearchPreview();
                OnPropertyChanged();
            }
        }
    }

    public bool HighlightMatches
    {
        get => _owner.SearchSettings.HighlightMatches;
        set
        {
            if (_owner.SearchSettings.HighlightMatches == value) return;
            _owner.SetSearchHighlightsEnabled(value);
            OnPropertyChanged();
        }
    }

    private bool HasQuery() => !string.IsNullOrEmpty(FindText);
    private bool CanReplace() => IsReplaceMode && HasQuery();

    [RelayCommand(CanExecute = nameof(HasQuery))]
    private Task FindNextAsync() => _owner.FindFromDialogAsync(reverse: false);

    [RelayCommand(CanExecute = nameof(HasQuery))]
    private Task FindPreviousAsync() => _owner.FindFromDialogAsync(reverse: true);

    [RelayCommand(CanExecute = nameof(CanReplace))]
    private Task ReplaceAsync() => _owner.ReplaceNextCommand.ExecuteAsync(null);

    [RelayCommand(CanExecute = nameof(CanReplace))]
    private Task ReplaceAllAsync() => _owner.ReplaceAllCommand.ExecuteAsync(null);

    public IAsyncRelayCommand RepeatDownCommand => _owner.FindNextCommand;
    public IAsyncRelayCommand RepeatUpCommand => _owner.FindPreviousCommand;

    [RelayCommand]
    private void Close(Window? window)
    {
        window?.Close();
    }
}
