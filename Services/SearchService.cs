using Notepad.NeoEdit;
using NotepadAvalonia.Models;
using System;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace NotepadAvalonia.Services;

/// <summary>
/// Maps to: function_14001cfac (find logic)
/// </summary>
public class SearchService
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(500);

    public (SearchResult? result, bool wrapped) FindNext(NeoEditor editor, SearchSettings settings)
    {
        return Find(editor, settings, forward: true);
    }

    public (SearchResult? result, bool wrapped) FindPrevious(NeoEditor editor, SearchSettings settings)
    {
        return Find(editor, settings, forward: false);
    }

    private (SearchResult? result, bool wrapped) Find(NeoEditor editor, SearchSettings settings, bool forward)
    {
        var text = editor.GetText();
        var searchText = settings.SearchString;

        if (string.IsNullOrEmpty(searchText)) return (null, false);

        int boundary = forward
            ? editor.SelectionStart + editor.SelectionLength
            : editor.SelectionStart;

        bool wrapped = false;

        if (settings.UseRegex)
        {
            var regex = CreateRegex(settings);
            var match = FindRegex(regex, text, boundary, forward, settings.WholeWord);
            if (match == null && settings.WrapAround && text.Length > 0)
            {
                wrapped = true;
                match = FindRegex(regex, text, forward ? 0 : text.Length, forward, settings.WholeWord);
            }
            return (match == null ? null : new SearchResult(match.Index, match.Length), wrapped);
        }
        else
        {
            var comparison = GetComparison(settings);
            int foundPos;

            if (forward)
            {
                foundPos = FindForward(text, searchText, boundary, comparison, settings.WholeWord);
                if (foundPos == -1 && settings.WrapAround && text.Length > 0)
                {
                    wrapped = true;
                    foundPos = FindForward(text, searchText, 0, comparison, settings.WholeWord);
                }
            }
            else
            {
                foundPos = FindBackward(text, searchText, boundary - 1, comparison, settings.WholeWord);
                if (foundPos == -1 && settings.WrapAround && text.Length > 0)
                {
                    wrapped = true;
                    foundPos = FindBackward(text, searchText, text.Length - 1, comparison, settings.WholeWord);
                }
            }

            if (foundPos >= 0)
                return (new SearchResult(foundPos, searchText.Length), wrapped);
        }

        return (null, wrapped);
    }

    public int ReplaceNext(NeoEditor editor, SearchSettings settings)
    {
        string searchText = settings.SearchString;
        if (string.IsNullOrEmpty(searchText)) return 0;

        string text = editor.GetText();
        int selectionStart = editor.SelectionStart;
        int selectionLength = editor.SelectionLength;
        int nextOffset = selectionStart + selectionLength;

        if (settings.UseRegex)
        {
            var regex = CreateRegex(settings);
            Match? match = null;
            if (selectionLength > 0)
            {
                var selectedMatch = regex.Match(text, selectionStart);
                if (selectedMatch.Success && selectedMatch.Index == selectionStart &&
                    selectedMatch.Length == selectionLength &&
                    (!settings.WholeWord || IsWholeWordMatch(text, selectionStart, selectionLength)))
                {
                    match = selectedMatch;
                }
            }

            match ??= FindRegex(regex, text, nextOffset, forward: true, settings.WholeWord);
            if (match == null && settings.WrapAround && nextOffset > 0)
                match = FindRegex(regex, text, 0, forward: true, settings.WholeWord);
            if (match == null) return 0;

            editor.ReplaceRange(match.Index, match.Length, match.Result(settings.ReplaceString));
            return 1;
        }

        var comparison = GetComparison(settings);
        bool selectionMatches = selectionLength == searchText.Length &&
            text.AsSpan(selectionStart, selectionLength).Equals(searchText.AsSpan(), comparison) &&
            (!settings.WholeWord || IsWholeWordMatch(text, selectionStart, selectionLength));
        int index = selectionMatches
            ? selectionStart
            : FindForward(text, searchText, nextOffset, comparison, settings.WholeWord);
        if (index < 0 && settings.WrapAround && nextOffset > 0)
            index = FindForward(text, searchText, 0, comparison, settings.WholeWord);
        if (index < 0) return 0;

        editor.ReplaceRange(index, searchText.Length, settings.ReplaceString);
        return 1;
    }

    public int ReplaceAll(NeoEditor editor, SearchSettings settings)
    {
        if (string.IsNullOrEmpty(settings.SearchString)) return 0;

        string text = editor.GetText();
        string newText;
        int count = 0;

        if (settings.UseRegex)
        {
            var regex = CreateRegex(settings);
            newText = regex.Replace(text, match =>
            {
                if (settings.WholeWord && !IsWholeWordMatch(text, match.Index, match.Length))
                    return match.Value;

                count++;
                return match.Result(settings.ReplaceString);
            });
        }
        else
        {
            var comparison = GetComparison(settings);
            int start = 0;
            var builder = new StringBuilder();
            int match;
            while ((match = FindForward(text, settings.SearchString, start, comparison, settings.WholeWord)) >= 0)
            {
                builder.Append(text, start, match - start);
                builder.Append(settings.ReplaceString);
                start = match + settings.SearchString.Length;
                count++;
            }
            if (count == 0) return 0;

            builder.Append(text, start, text.Length - start);
            newText = builder.ToString();
        }

        if (count > 0 && newText != text)
        {
            int caret = editor.CaretOffset;
            editor.ReplaceRange(0, text.Length, newText);
            editor.SetSelection(Math.Min(caret, newText.Length), 0);
        }
        return count;
    }

    private static Regex CreateRegex(SearchSettings settings) =>
        new(settings.SearchString, settings.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase, RegexTimeout);

    private static StringComparison GetComparison(SearchSettings settings) =>
        settings.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private Match? FindRegex(Regex regex, string text, int boundary, bool forward, bool wholeWord)
    {
        if (!forward && boundary == 0) return null;

        long started = Stopwatch.GetTimestamp();
        Match? last = null;
        for (var match = regex.Match(text, forward ? boundary : 0); match.Success; match = match.NextMatch())
        {
            // The regex timeout applies to each match. Also bound a scan across many matches.
            if (Stopwatch.GetElapsedTime(started) >= RegexTimeout)
                throw new RegexMatchTimeoutException(text, regex.ToString(), RegexTimeout);

            if (!forward && (match.Index >= boundary || match.Length > boundary - match.Index))
                break;
            if (wholeWord && !IsWholeWordMatch(text, match.Index, match.Length))
                continue;
            if (forward) return match;
            last = match;
        }
        return last;
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
            index++;
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
