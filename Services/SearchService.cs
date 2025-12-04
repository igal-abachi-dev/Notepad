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
                foundPos = FindForward(text, searchText, startPos, comparison, settings.WholeWord);
                if (foundPos == -1 && settings.WrapAround)
                    foundPos = FindForward(text, searchText, 0, comparison, settings.WholeWord);
            }
            else
            {
                foundPos = FindBackward(text, searchText, startPos, comparison, settings.WholeWord);
                if (foundPos == -1 && settings.WrapAround)
                    foundPos = FindBackward(text, searchText, text.Length - 1, comparison, settings.WholeWord);
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
        if (string.IsNullOrEmpty(settings.SearchString))
        {
            return 0;
        }

        int count = 0;
        var text = editor.Document.Text;

        if (settings.UseRegex)
        {
            var options = settings.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
            var regex = new Regex(settings.SearchString, options);
            var matches = regex.Matches(text);
            count = matches.Count;
            if (count > 0)
            {
                text = regex.Replace(text, settings.ReplaceString);
            }
        }
        else
        {
            var comparison = settings.MatchCase
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            int index = 0;
            while ((index = text.IndexOf(settings.SearchString, index, comparison)) >= 0)
            {
                if (settings.WholeWord && !IsWholeWordMatch(text, index, settings.SearchString.Length))
                {
                    index += settings.SearchString.Length;
                    continue;
                }

                text = text.Remove(index, settings.SearchString.Length)
                           .Insert(index, settings.ReplaceString);
                index += settings.ReplaceString.Length;
                count++;
            }
        }

        if (count > 0)
        {
            editor.Document.Text = text;
        }

        return count;
    }

    private int FindForward(string text, string searchText, int startPos, StringComparison comparison, bool wholeWord)
    {
        int index = startPos;
        while (index <= text.Length)
        {
            index = text.IndexOf(searchText, index, comparison);
            if (index < 0) break;
            if (!wholeWord || IsWholeWordMatch(text, index, searchText.Length))
            {
                return index;
            }
            index += searchText.Length;
        }

        return -1;
    }

    private int FindBackward(string text, string searchText, int startPos, StringComparison comparison, bool wholeWord)
    {
        int index = startPos;
        while (index >= 0)
        {
            index = text.LastIndexOf(searchText, index, comparison);
            if (index < 0) break;
            if (!wholeWord || IsWholeWordMatch(text, index, searchText.Length))
            {
                return index;
            }
            index -= 1;
        }

        return -1;
    }

    private bool IsWholeWordMatch(string text, int index, int length)
    {
        int before = index - 1;
        int after = index + length;

        bool startBoundary = before < 0 || !IsWordChar(text[before]);
        bool endBoundary = after >= text.Length || !IsWordChar(text[after]);

        return startBoundary && endBoundary;
    }

    private bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
public record SearchResult(int StartOffset, int Length);
