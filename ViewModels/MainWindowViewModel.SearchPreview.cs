using Notepad.NeoEdit;
using NotepadAvalonia.Models;
using NotepadAvalonia.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NotepadAvalonia.ViewModels;

public partial class MainWindowViewModel
{
    private const int MaximumPreviewSpan = 64 * 1024;
    private CancellationTokenSource? _searchPreviewRequest;

    private void OnSearchViewportChanged(object? sender, EventArgs e) => QueueSearchPreview();

    internal void SetSearchHighlightsEnabled(bool enabled)
    {
        SearchSettings.HighlightMatches = enabled;
        RefreshSearchPreview();
        PersistSettings();
    }

    internal void RefreshSearchPreview()
    {
        UpdateStatus();
        ReportSearchFeedback(null);
        StopSearchPreview();
        QueueSearchPreview();
    }

    private void StopSearchPreview()
    {
        _searchPreviewRequest?.Cancel();
        _searchPreviewRequest = null;
        EditorControl?.ClearSearchHighlights();
    }

    private void QueueSearchPreview()
    {
        if (!SearchSettings.HighlightMatches || _findReplaceDialog == null || EditorControl is not { } editor ||
            string.IsNullOrEmpty(SearchSettings.SearchString)) return;
        _searchPreviewRequest?.Cancel();
        var request = new CancellationTokenSource();
        _searchPreviewRequest = request;
        _ = UpdateSearchPreviewAsync(editor, request);
    }

    private async Task UpdateSearchPreviewAsync(NeoEditor editor, CancellationTokenSource request)
    {
        var cancellation = request.Token;
        try
        {
            await Task.Delay(120, cancellation);
            var viewport = editor.GetVisibleTextRange();
            int start = viewport.Start;
            int end = (int)Math.Min(viewport.End, (long)start + MaximumPreviewSpan);
            long revision = editor.DocumentVersion;
            var settings = new SearchSettings
            {
                SearchString = SearchSettings.SearchString,
                MatchCase = SearchSettings.MatchCase,
                UseRegex = SearchSettings.UseRegex,
                WholeWord = SearchSettings.WholeWord
            };

            // Reuse an existing snapshot for regex anchors and lookarounds. When a large
            // edit invalidates it, use a bounded slice for literals until Find needs it again.
            string? text = editor.GetSearchPreviewSnapshot();
            int baseOffset = 0;
            if (text == null)
            {
                if (settings.UseRegex) return;
                baseOffset = Math.Max(0, start - SearchService.PreviewContextLength);
                int limit = (int)Math.Min(editor.TextLength, (long)end + SearchService.PreviewContextLength);
                text = editor.GetSearchPreviewText(baseOffset, limit - baseOffset);
            }

            var matches = await Task.Run(() => SearchService.FindPreviewMatches(text, settings,
                start - baseOffset, end - baseOffset, baseOffset, cancellation), cancellation);
            if (!cancellation.IsCancellationRequested && editor.DocumentVersion == revision)
                editor.SetPassiveSearchMatches(revision, matches);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (_searchPreviewRequest == request) _searchPreviewRequest = null;
            request.Dispose();
        }
    }
}
