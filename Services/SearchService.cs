using AvaloniaEdit;
using AvaloniaEdit.Document;
using NotepadAvalonia.Models;
using System;
using System.Text.RegularExpressions;

namespace NotepadAvalonia.Services;

/// <summary>
/// Maps to: function_14001cfac (find logic)
/// Uses AvaloniaEdit's document model
/// </summary>
public class SearchService
{
    public SearchResult? FindNext(TextEditor editor, SearchSettings settings)
    {
        return Find(editor, settings, forward: true);
    }

    public SearchResult? FindPrevious(TextEditor editor, SearchSettings settings)
    {
        return Find(editor, settings, forward: false);
    }

    private SearchResult? Find(TextEditor editor, SearchSettings settings, bool forward)
    {
        var document = editor.Document;
        var text = document.Text;
        var searchText = settings.SearchString;

        if (string.IsNullOrEmpty(searchText)) return null;

        int startPos = forward
            ? editor.SelectionStart + editor.SelectionLength
            : editor.SelectionStart;

        int foundPos;

        if (settings.UseRegex)
        {
            var options = settings.MatchCase
                ? RegexOptions.None
                : RegexOptions.IgnoreCase;
            var regex = new Regex(searchText, options);

            if (forward)
            {
                var match = regex.Match(text, startPos);
                if (!match.Success && settings.WrapAround)
                    match = regex.Match(text, 0);
                if (match.Success)
                    return new SearchResult(match.Index, match.Length);
            }
            else
            {
                var matches = regex.Matches(text[..startPos]);
                if (matches.Count > 0)
                {
                    var last = matches[^1];
                    return new SearchResult(last.Index, last.Length);
                }
                if (settings.WrapAround)
                {
                    matches = regex.Matches(text);
                    if (matches.Count > 0)
                    {
                        var last = matches[^1];
                        return new SearchResult(last.Index, last.Length);
                    }
                }
            }
        }
        else
        {
            var comparison = settings.MatchCase
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            if (forward)
            {
                foundPos = text.IndexOf(searchText, startPos, comparison);
                if (foundPos == -1 && settings.WrapAround)
                    foundPos = text.IndexOf(searchText, 0, comparison);
            }
            else
            {
                foundPos = text.LastIndexOf(searchText, startPos, comparison);
                if (foundPos == -1 && settings.WrapAround)
                    foundPos = text.LastIndexOf(searchText, comparison);
            }

            if (foundPos >= 0)
                return new SearchResult(foundPos, searchText.Length);
        }

        return null;
    }

    public int ReplaceNext(TextEditor editor, SearchSettings settings)
    {
        var result = FindNext(editor, settings);
        if (result != null)
        {
            editor.Document.Replace(result.StartOffset, result.Length, settings.ReplaceString);
            return 1;
        }
        return 0;
    }

    public int ReplaceAll(TextEditor editor, SearchSettings settings)
    {
        int count = 0;
        var document = editor.Document;

        // Work backwards to avoid offset issues
        var text = document.Text;
        var searchText = settings.SearchString;
        var comparison = settings.MatchCase
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        int pos = text.Length;
        while ((pos = text.LastIndexOf(searchText, pos - 1, comparison)) >= 0)
        {
            document.Replace(pos, searchText.Length, settings.ReplaceString);
            text = document.Text;
            count++;
            if (pos == 0) break;
        }

        return count;
    }
}
public record SearchResult(int StartOffset, int Length);