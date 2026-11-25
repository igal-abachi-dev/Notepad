using Notepad.NeoEdit;
using NotepadAvalonia.Models;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NotepadAvalonia.Services;

public class SearchService
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(500);
    private ZeroWidthMatch? _lastZeroWidthMatch;

    private sealed record ZeroWidthMatch(NeoEditor Editor, long Version, long SelectionVersion, int Offset, string Query,
        bool Forward, bool MatchCase, bool WholeWord);

    public (SearchResult? result, bool wrapped) FindNext(NeoEditor editor, SearchSettings settings) =>
        Find(editor, settings, forward: true);

    public (SearchResult? result, bool wrapped) FindPrevious(NeoEditor editor, SearchSettings settings) =>
        Find(editor, settings, forward: false);

    private (SearchResult? result, bool wrapped) Find(NeoEditor editor, SearchSettings settings, bool forward)
    {
        string text = editor.GetText();
        if (string.IsNullOrEmpty(settings.SearchString)) return (null, false);

        int boundary = editor.SelectionStart + (forward ? editor.SelectionLength : 0);
        var key = new ZeroWidthMatch(editor, editor.DocumentVersion, editor.SelectionVersion, boundary, settings.SearchString,
            forward, settings.MatchCase, settings.WholeWord);
        int? excluded = settings.UseRegex && editor.SelectionLength == 0 && _lastZeroWidthMatch == key
            ? boundary : null;

        var regex = settings.UseRegex ? CreateRegex(settings, forward) : null;
        var result = FindInRange(text, settings, regex, forward ? boundary : 0,
            forward ? text.Length : boundary, forward, excluded);
        bool wrapped = false;
        if (result == null && settings.WrapAround && (forward ? boundary > 0 : boundary < text.Length))
        {
            result = FindInRange(text, settings, regex, forward ? 0 : boundary,
                forward ? boundary : text.Length, forward, excluded);
            wrapped = result != null;
        }

        if (result is { Length: 0 })
        {
            long nextSelectionVersion = editor.SelectionVersion +
                (editor.SelectionLength == 0 && editor.CaretOffset == result.StartOffset ? 0 : 1);
            _lastZeroWidthMatch = key with { Offset = result.StartOffset, SelectionVersion = nextSelectionVersion };
        }
        else if (result != null || excluded == null)
            _lastZeroWidthMatch = null;
        return (result, wrapped);
    }

    public int ReplaceNext(NeoEditor editor, SearchSettings settings) =>
        ReplaceNext(editor, settings, out _, out _);

    public int ReplaceNext(NeoEditor editor, SearchSettings settings, out bool found, out bool wrapped)
    {
        found = wrapped = false;
        string query = settings.SearchString;
        if (string.IsNullOrEmpty(query)) return 0;

        string text = editor.GetText();
        int start = editor.SelectionStart;
        int length = editor.SelectionLength;
        string replacement = settings.ReplaceString;
        bool selected;
        if (settings.UseRegex)
        {
            var match = CreateRegex(settings, forward: true).Match(text, start);
            var key = new ZeroWidthMatch(editor, editor.DocumentVersion, editor.SelectionVersion, start, query, true,
                settings.MatchCase, settings.WholeWord);
            selected = match.Success && match.Index == start && match.Length == length &&
                (length > 0 || _lastZeroWidthMatch == key) &&
                IsEligible(text, start, length, settings.WholeWord, protectCrLf: true);
            if (selected) replacement = match.Result(replacement);
        }
        else
        {
            selected = length == query.Length &&
                text.AsSpan(start, length).Equals(query, GetComparison(settings)) &&
                IsEligible(text, start, length, settings.WholeWord, protectCrLf: true);
        }

        if (selected)
        {
            editor.ReplaceRange(start, length, replacement);
            if (length == 0)
            {
                // An insertion can move the zero-width match without advancing past it.
                _lastZeroWidthMatch = new(editor, editor.DocumentVersion, editor.SelectionVersion, editor.CaretOffset, query,
                    true, settings.MatchCase, settings.WholeWord);
            }
        }

        var (next, didWrap) = FindNext(editor, settings);
        found = next != null;
        wrapped = didWrap;
        if (next != null) editor.SetSelection(next.StartOffset, next.Length);
        return selected ? 1 : 0;
    }

    public int ReplaceAll(NeoEditor editor, SearchSettings settings)
    {
        string query = settings.SearchString;
        if (string.IsNullOrEmpty(query)) return 0;

        string text = editor.GetText();
        int oldCaret = editor.CaretOffset;
        int newCaret = oldCaret;
        int count = 0;
        int copied = 0;
        var result = new StringBuilder();
        void AppendReplacement(int index, int length, string replacement)
        {
            result.Append(text, copied, index - copied);
            if (oldCaret >= index + length)
                newCaret += replacement.Length - length;
            else if (oldCaret > index)
                newCaret = result.Length + replacement.Length;
            result.Append(replacement);
            copied = index + length;
            count++;
        }

        if (settings.UseRegex)
        {
            var regex = CreateRegex(settings, forward: true);
            long started = Stopwatch.GetTimestamp();
            for (var match = regex.Match(text); match.Success; match = match.NextMatch())
            {
                CheckTimeout(started, text, regex);
                if (IsEligible(text, match.Index, match.Length, settings.WholeWord, protectCrLf: false))
                    AppendReplacement(match.Index, match.Length, match.Result(settings.ReplaceString));
            }
        }
        else
        {
            int start = 0;
            var comparison = GetComparison(settings);
            while (start <= text.Length - query.Length)
            {
                int index = text.IndexOf(query, start, comparison);
                if (index < 0) break;
                if (IsEligible(text, index, query.Length, settings.WholeWord, protectCrLf: false))
                {
                    AppendReplacement(index, query.Length, settings.ReplaceString);
                    start = index + query.Length;
                }
                else
                    start = index + 1;
            }
        }

        if (count == 0) return 0;
        result.Append(text, copied, text.Length - copied);
        editor.ReplaceRange(0, text.Length, result.ToString(), newCaret);
        return count;
    }

    private static SearchResult? FindInRange(string text, SearchSettings settings, Regex? regex,
        int start, int end, bool forward, int? excludedZeroWidth)
    {
        if (regex != null)
        {
            long started = Stopwatch.GetTimestamp();
            for (var match = regex.Match(text, forward ? start : end); match.Success;)
            {
                CheckTimeout(started, text, regex);
                if (forward ? match.Index > end : match.Index < start) break;
                if (match.Index >= start && match.Length <= end - match.Index &&
                    (match.Length != 0 || match.Index != excludedZeroWidth) &&
                    IsEligible(text, match.Index, match.Length, settings.WholeWord, protectCrLf: true))
                    return new(match.Index, match.Length);

                int retry = forward ? match.Index + 1 : match.Index + Math.Max(0, match.Length) - 1;
                if (retry < start || retry > end) break;
                match = regex.Match(text, retry);
            }
            return null;
        }

        var query = settings.SearchString.AsSpan();
        var comparison = GetComparison(settings);
        while (end - start >= query.Length)
        {
            var window = text.AsSpan(start, end - start);
            int offset = forward ? window.IndexOf(query, comparison) : window.LastIndexOf(query, comparison);
            if (offset < 0) break;
            int index = start + offset;
            if (IsEligible(text, index, query.Length, settings.WholeWord, protectCrLf: true))
                return new(index, query.Length);

            if (forward) start = index + 1;
            else end = index + query.Length - 1;
        }

        return null;
    }

    private static Regex CreateRegex(SearchSettings settings, bool forward)
    {
        var options = settings.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
        if (!forward) options |= RegexOptions.RightToLeft;
        return new(settings.SearchString, options, RegexTimeout);
    }

    private static void CheckTimeout(long started, string text, Regex regex)
    {
        if (Stopwatch.GetElapsedTime(started) >= RegexTimeout)
            throw new RegexMatchTimeoutException(text, regex.ToString(), RegexTimeout);
    }

    private static StringComparison GetComparison(SearchSettings settings) =>
        settings.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private static bool IsEligible(string text, int start, int length, bool wholeWord, bool protectCrLf)
    {
        int end = start + length;
        return IsBoundary(text, start, protectCrLf) && IsBoundary(text, end, protectCrLf) &&
            (!wholeWord || (!IsWordCharacter(text, start - 1) && !IsWordCharacter(text, end)));
    }

    private static bool IsBoundary(string text, int offset, bool protectCrLf) =>
        offset <= 0 || offset >= text.Length ||
        (!char.IsSurrogatePair(text[offset - 1], text[offset]) &&
         (!protectCrLf || text[offset - 1] != '\r' || text[offset] != '\n'));

    private static bool IsWordCharacter(string text, int offset)
    {
        if (offset < 0 || offset >= text.Length) return false;
        if (offset > 0 && char.IsSurrogatePair(text[offset - 1], text[offset])) offset--;
        return char.GetUnicodeCategory(text, offset) is
            UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or
            UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber or
            UnicodeCategory.ConnectorPunctuation or UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark;
    }
}

public record SearchResult(int StartOffset, int Length);
