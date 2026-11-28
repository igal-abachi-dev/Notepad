using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NotepadAvalonia.Models;
using System;
using System.IO;
using System.Threading.Tasks;

namespace NotepadAvalonia.ViewModels;

public partial class MainWindowViewModel
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditorReadOnly))]
    private bool _isReadOnlyMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditorReadOnly))]
    private bool _isFollowingLog;

    private DispatcherTimer? _followTimer;
    private long _followGeneration;
    private bool _followReading;
    private (long Length, DateTime Modified, DateTime Created)? _followObserved;

    public bool IsEditorReadOnly => IsReadOnlyMode || IsFollowingLog;

    [RelayCommand]
    private void ToggleReadOnlyMode()
    {
        IsReadOnlyMode = !IsReadOnlyMode;
        UpdateStatus();
        NotifyEditingAvailability();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ToggleTailFileAsync()
    {
        if (IsFollowingLog)
        {
            StopFollowingFile();
            return;
        }
        if (Document.IsUntitled || EditorControl == null) return;
        if (Document.IsModified || HasPendingSave)
        {
            UpdateStatus();
            StatusText += "    Save edits before tailing the file.";
            return;
        }
        _followGeneration++;
        _followObserved = null;
        IsFollowingLog = true;
        EditorControl.CaretOffset = EditorControl.TextLength;
        _followTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background,
            async (_, _) => await PollFollowFileAsync());
        _followTimer.Start();
        NotifyEditingAvailability();
        UpdateStatus();
        await PollFollowFileAsync();
    }

    private void StopFollowingFile()
    {
        if (!IsFollowingLog) return;
        _followTimer?.Stop();
        _followGeneration++;
        _followObserved = null;
        IsFollowingLog = false;
        NotifyEditingAvailability();
        UpdateStatus();
    }

    private void NotifyEditingAvailability()
    {
        CutCommand.NotifyCanExecuteChanged();
        PasteCommand.NotifyCanExecuteChanged();
        NotifyCommentAvailability();
        DeleteCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        RedoCommand.NotifyCanExecuteChanged();
    }

    private async Task PollFollowFileAsync()
    {
        if (!IsFollowingLog || _followReading || EditorControl is not { } editor) return;
        var document = Document;
        string path = document.FilePath;
        long generation = _followGeneration;
        _followReading = true;
        try
        {
            var observed = await Task.Run(() =>
            {
                var info = new FileInfo(path);
                return (info.Length, info.LastWriteTimeUtc, info.CreationTimeUtc);
            });
            if (!IsCurrent() || observed == _followObserved) return;
            var encoding = document.EncodingType;
            bool windowsOnly = Settings.WindowsOnlyLineEndings;
            var file = await Task.Run(() => _fileService.ReadDocumentAsync(path, encoding, windowsOnly));
            if (!IsCurrent()) return;
            _followObserved = observed;
            if (document.SavedFileVersion == file.Version && document.SavedFileVersion?.ResolvedPath == file.Version.ResolvedPath) return;

            document.SavedFileVersion = file.Version;
            document.IsReadOnlyFile = file.IsReadOnly;
            document.LineEnding = file.LineEnding;
            editor.NewLine = windowsOnly ? "\r\n" : file.LineEnding switch
            {
                LineEndingStyle.CR => "\r",
                LineEndingStyle.LF => "\n",
                _ => "\r\n"
            };
            editor.UpdateFollowedText(file.Content);
            UpdateEncodingDisplay();
            UpdateStatus();
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            if (IsCurrent())
            {
                _followObserved = null;
                UpdateStatus();
                StatusText += "    Waiting for readable file data…";
            }
        }
        finally { _followReading = false; }

        bool IsCurrent() => IsFollowingLog && _followGeneration == generation && Document == document && document.FilePath == path &&
            !document.IsModified && EditorControl == editor;
    }
}
