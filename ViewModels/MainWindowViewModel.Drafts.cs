using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Notepad.NeoEdit;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using NotepadAvalonia.Views;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace NotepadAvalonia.ViewModels;

public partial class MainWindowViewModel
{
    private static readonly TimeSpan DraftInterval = TimeSpan.FromSeconds(3);
    private readonly DraftStore? _draftStore;
    private DispatcherTimer? _draftTimer;

    [ObservableProperty]
    private string? _draftWarning;

    public bool SupportsDraftRecovery => _draftStore != null;
    internal bool HasDraftCleanup => !Document.IsModified && Document.Draft != null;

    private void ScheduleDraftCheckpoint()
    {
        if (_draftStore == null || (!Document.IsModified && Document.Draft == null)) return;
        if (_draftTimer == null)
        {
            _draftTimer = new DispatcherTimer { Interval = DraftInterval };
            _draftTimer.Tick += async (_, _) =>
            {
                _draftTimer.Stop();
                await CheckpointDraftAsync();
            };
        }
        if (!_draftTimer.IsEnabled) _draftTimer.Start();
    }

    private Task CaptureDraftWrite(bool force = false)
    {
        if (_draftStore == null || !Document.IsModified || EditorControl is not { } editor)
            return Task.CompletedTask;

        var document = Document;
        var lease = document.Draft ??= _draftStore.Create();
        long version = editor.DocumentVersion;
        if (!force && document.DraftVersion == version) return Task.CompletedTask;
        var snapshot = new DraftSnapshot(editor.GetText(), document.IsUntitled ? null : document.FilePath,
            document.SaveEncodingType, document.LineEnding, editor.SelectionAnchor, editor.CaretOffset,
            document.SavedFileVersion, _timeProvider.GetUtcNow());
        return lease.WriteAsync(snapshot, ++document.DraftSequence);
    }

    internal async Task<bool> CheckpointDraftAsync(bool force = false)
    {
        if (_draftStore == null || EditorControl is not { } editor) return false;
        var document = Document;
        try
        {
            if (!document.IsModified)
                await ReleaseDocumentDraftAsync(document, discard: true);
            else
            {
                long version = editor.DocumentVersion;
                var write = CaptureDraftWrite(force);
                var lease = document.Draft;
                long sequence = document.DraftSequence;
                await write;
                if (document.Draft == lease && document.DraftSequence == sequence)
                    document.DraftVersion = Math.Max(document.DraftVersion, version);
            }
            DraftWarning = null;
            return true;
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            Trace.TraceWarning("Draft checkpoint failed: {0}", ex.GetType().Name);
            DraftWarning = "Draft recovery could not save the latest changes.";
            ScheduleDraftCheckpoint();
            return false;
        }
    }

    internal Task CheckpointForSystemShutdownAsync() =>
        HasDraftCleanup ? Document.Draft!.DiscardAsync() : CaptureDraftWrite();

    internal async Task<bool> ConfirmWindowCloseAsync()
    {
        if (_draftStore == null) return await CheckSaveChangesAsync();
        if (_savePickerPending || _saveDecisionPending) return false;
        while (_saveIdle is { } idle)
            await idle.Task;
        if (!Document.IsModified)
        {
            await ReleaseDocumentDraftAsync(Document, discard: true);
            return true;
        }
        while (Document.IsModified)
        {
            var editor = EditorControl;
            long? revision = editor?.DocumentVersion;
            if (!await CheckpointDraftAsync(force: true))
                return await CheckSaveChangesAsync(discardDraft: true);
            if (EditorControl == editor && editor?.DocumentVersion == revision) return true;
        }
        return true;
    }

    internal void ReleaseAfterWindowClosed()
    {
        _draftTimer?.Stop();
        _ = ReleaseDocumentDraftAsync(Document, discard: !Document.IsModified);
    }

    private async Task ReleaseDocumentDraftAsync(DocumentModel document, bool discard)
    {
        if (document.Draft is not { } lease) return;
        document.Draft = null;
        document.DraftVersion = -1;
        try
        {
            if (discard) await lease.DiscardAsync();
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            Trace.TraceWarning("Draft cleanup failed: {0}", ex.GetType().Name);
            DraftWarning = "A recovery draft could not be removed.";
        }
        finally
        {
            await lease.ReleaseAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(SupportsDraftRecovery))]
    private async Task RecoverDraftsAsync()
    {
        if (_draftStore == null || OwnerWindow is not { } owner) return;
        try
        {
            var drafts = await _draftStore.DiscoverAsync();
            var picker = new RecoverDraftDialog(drafts);
            var selection = await picker.ShowDialog<DraftInfo?>(owner);
            if (selection != null && !await RecoverDraftAsync(selection.Id))
                await ShowErrorAsync("This draft is already open or is no longer available.");
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            await ShowErrorAsync($"Cannot read recovery drafts. {ex.Message}");
        }
    }

    internal async Task<bool> RecoverDraftAsync(Guid id)
    {
        if (_draftStore?.TryClaim(id) is not { } lease) return false;
        bool accepted = false;
        try
        {
            var snapshot = await Task.Run(lease.Read);
            if (snapshot == null) return false;

            if (!Document.IsUntitled || Document.IsModified || EditorControl?.TextLength > 0)
            {
                if (OwnerWindow == null) return false;
                var viewModel = new MainWindowViewModel(_fileService, new SearchService(), _settingsService,
                    _printService, _timeProvider, _draftStore);
                viewModel.ApplyDraft(snapshot, lease);
                var window = new MainWindow { DataContext = viewModel };
                window.Show();
            }
            else
                ApplyDraft(snapshot, lease);
            accepted = true;
            return true;
        }
        finally
        {
            if (!accepted) await lease.ReleaseAsync();
        }
    }

    private DraftSnapshot? _pendingDraftSelection;

    private void ApplyDraft(DraftSnapshot snapshot, DraftLease lease)
    {
        _loadRequest++;
        var previous = Document;
        bool named = snapshot.Path != null && snapshot.SavedFileVersion != null;
        Document = new DocumentModel
        {
            FilePath = named ? snapshot.Path! : "",
            IsUntitled = !named,
            IsModified = true,
            Encoding = _fileService.GetEncoding(snapshot.Encoding),
            EncodingType = snapshot.Encoding,
            SaveEncodingType = snapshot.Encoding,
            LineEnding = snapshot.LineEnding,
            SavedFileVersion = snapshot.SavedFileVersion,
            Draft = lease
        };
        _pendingDraftSelection = snapshot;
        CurrentLanguage = named ? SyntaxMatcher.DetectLanguage(snapshot.Path!) : SyntaxLanguage.Plain;
        ResetEditor(snapshot.Content);
        UpdateEncodingDisplay();
        UpdateStatus();
        _ = ReleaseDocumentDraftAsync(previous, discard: true);
    }
}
