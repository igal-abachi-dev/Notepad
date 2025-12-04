using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NotepadAvalonia.Models;

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

    public string FindText
    {
        get => _owner.SearchSettings.SearchString;
        set
        {
            if (_owner.SearchSettings.SearchString != value)
            {
                _owner.SearchSettings.SearchString = value ?? string.Empty;
                OnPropertyChanged();
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

    public bool SearchUp
    {
        get => _owner.SearchSettings.SearchUp;
        set
        {
            if (_owner.SearchSettings.SearchUp != value)
            {
                _owner.SearchSettings.SearchUp = value;
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
                OnPropertyChanged();
            }
        }
    }

    [RelayCommand]
    private void FindNext()
    {
        _owner.FindNextCommand.Execute(null);
    }

    [RelayCommand]
    private void Replace()
    {
        if (IsFindMode) return;
        _owner.ReplaceNextCommand.Execute(null);
    }

    [RelayCommand]
    private void ReplaceAll()
    {
        if (IsFindMode) return;
        _owner.ReplaceAllCommand.Execute(null);
    }

    [RelayCommand]
    private void Close(Window? window)
    {
        window?.Close();
    }
}
