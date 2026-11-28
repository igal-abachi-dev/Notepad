using Notepad.NeoEdit;
using NotepadAvalonia.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace NotepadAvalonia.Services;

public class SearchService
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan PreviewTimeout = TimeSpan.FromMilliseconds(50);
    internal const int PreviewContextLength = 4096;
    private ZeroWidthMatch? _lastZeroWidthMatch;

    private sealed record ZeroWidthMatch(NeoEditor Editor, long Version, long SelectionVersion, int Offset, string Query,
        bool Forward, bool MatchCase, bool WholeWord);

    public (SearchResult? result, bool wrapped) FindNext(NeoEditor editor, SearchSettings settings) =>
        Find(editor, settings, forward: true);

    public (SearchResult? result, bool wrapped) FindPrevious(NeoEditor editor, SearchSettings settings) =>
        Find(editor, settings, forward: false);

    private (SearchResult? result, bool wrapped) Find(NeoEditor editor, SearchSettings settings, bool forward)
    {
        if (string.IsNullOrEmpty(settings.SearchString)) return (null, false);
        string text = editor.GetText();

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

    public int ReplaceNext(NeoEditor editor, SearchSettings settings, out bool found, out bool wrapped) =>
        ReplaceNext(editor, settings, out found, out wrapped, out _);

    internal int ReplaceNext(NeoEditor editor, SearchSettings settings, out bool found, out bool wrapped,
        out bool followUpTimedOut)
    {
        found = wrapped = followUpTimedOut = false;
        if (editor.IsReadOnly) return 0;
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
            if (selected) replacement = match.Result(ExpandReplacementEscapes(replacement));
        }
        else
        {
            selected = length > 0 &&
                new LiteralSearch(settings).Matches(text.AsSpan(start), length) &&
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

        SearchResult? next;
        try { (next, wrapped) = FindNext(editor, settings); }
        catch (RegexMatchTimeoutException) when (selected)
        {
            followUpTimedOut = true;
            return 1;
        }
        found = next != null;
        if (next != null)
        {
            if (settings.HighlightMatches) editor.SelectSearchMatch(next.StartOffset, next.Length);
            else editor.SetSelection(next.StartOffset, next.Length);
        }
        else editor.ClearActiveSearchMatch();
        return selected ? 1 : 0;
    }

    public int ReplaceAll(NeoEditor editor, SearchSettings settings)
    {
        if (editor.IsReadOnly) return 0;
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
            string replacement = ExpandReplacementEscapes(settings.ReplaceString);
            long started = Stopwatch.GetTimestamp();
            for (var match = regex.Match(text); match.Success; match = match.NextMatch())
            {
                CheckTimeout(started, text, regex);
                if (IsEligible(text, match.Index, match.Length, settings.WholeWord, protectCrLf: match.Length == 0))
                    AppendReplacement(match.Index, match.Length, match.Result(replacement));
            }
        }
        else
        {
            var literal = new LiteralSearch(settings);
            for (int start = 0; start < text.Length;)
            {
                var match = FindLiteral(text, literal, start, text.Length, forward: true,
                    settings.WholeWord, protectCrLf: false);
                if (match == null) break;
                AppendReplacement(match.StartOffset, match.Length, settings.ReplaceString);
                start = match.StartOffset + match.Length;
            }
        }

        if (count == 0) return 0;
        result.Append(text, copied, text.Length - copied);
        editor.ReplaceRange(0, text.Length, result.ToString(), newCaret);
        return count;
    }

    private static string ExpandReplacementEscapes(string replacement)
    {
        if (!replacement.Contains('\\')) return replacement;
        var expanded = new StringBuilder(replacement.Length);
        for (int i = 0; i < replacement.Length; i++)
        {
            char current = replacement[i];
            if (current == '\\' && i + 1 < replacement.Length)
            {
                char next = replacement[i + 1];
                if (next is 'r' or 'n' or 't' or '\\')
                {
                    current = next switch { 'r' => '\r', 'n' => '\n', 't' => '\t', _ => '\\' };
                    i++;
                }
            }
            expanded.Append(current);
        }
        return expanded.ToString();
    }

    internal static IReadOnlyList<(int Start, int Length)> FindPreviewMatches(string text, SearchSettings settings,
        int start, int end, int baseOffset, CancellationToken cancellationToken)
    {
        var matches = new List<(int Start, int Length)>();
        if (string.IsNullOrEmpty(settings.SearchString) || settings.SearchString.Length > PreviewContextLength) return matches;

        long started = Stopwatch.GetTimestamp();
        try
        {
            var regex = settings.UseRegex ? CreateRegex(settings, forward: true, timeout: PreviewTimeout) : null;
            var literal = regex == null ? new LiteralSearch(settings) : default;
            int context = settings.UseRegex ? PreviewContextLength :
                (int)Math.Clamp((long)settings.SearchString.Length * 2, 32, PreviewContextLength);
            int searchEnd = (int)Math.Min(text.Length, (long)end + context);
            for (int offset = Math.Max(0, start - context); offset <= end && matches.Count < 512;)
            {
                if (cancellationToken.IsCancellationRequested || Stopwatch.GetElapsedTime(started) >= PreviewTimeout)
                    break;
                var match = regex != null
                    ? FindInRange(text, settings, regex, offset, text.Length, forward: true, excludedZeroWidth: null)
                    : FindLiteral(text, literal, offset, searchEnd, forward: true, settings.WholeWord, protectCrLf: true);
                if (match == null || match.StartOffset > end ||
                    (match.StartOffset == end && (match.Length != 0 || end < text.Length))) break;
                if (match.Length == 0 ? match.StartOffset >= start : match.StartOffset + match.Length > start)
                    matches.Add((match.StartOffset + baseOffset, match.Length));
                if (match.StartOffset == text.Length) break;
                offset = match.StartOffset + Math.Max(1, match.Length);
            }
        }
        catch (Exception ex) when (ex is RegexParseException or RegexMatchTimeoutException)
        {
            // Preview is optional. Explicit search still reports invalid or slow patterns.
        }
        return matches;
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

        return FindLiteral(text, new LiteralSearch(settings), start, end, forward,
            settings.WholeWord, protectCrLf: true);
    }

    private static SearchResult? FindLiteral(string text, LiteralSearch literal, int start, int end,
        bool forward, bool wholeWord, bool protectCrLf)
    {
        while (start < end)
        {
            int offset = literal.Find(text.AsSpan(start, end - start), forward, out int length);
            if (offset < 0 || length == 0) break;
            int index = start + offset;
            if ((forward || literal.ValidateBackwardLength(text.AsSpan(index, end - index), ref length)) &&
                literal.CanConsume(text.AsSpan(index), length) && IsEligible(text, index, length, wholeWord, protectCrLf))
                return new(index, length);

            if (forward) start = index + 1;
            else end = index + length - 1;
        }

        return null;
    }

    private static Regex CreateRegex(SearchSettings settings, bool forward, TimeSpan? timeout = null)
    {
        var options = settings.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
        if (!forward) options |= RegexOptions.RightToLeft;
        return new(settings.SearchString, options, timeout ?? RegexTimeout);
    }

    private static void CheckTimeout(long started, string text, Regex regex)
    {
        if (Stopwatch.GetElapsedTime(started) >= regex.MatchTimeout)
            throw new RegexMatchTimeoutException(text, regex.ToString(), regex.MatchTimeout);
    }

    private readonly struct LiteralSearch
    {
        private readonly string _query;
        private readonly CompareInfo _compare;
        private readonly CompareOptions _options;
        private readonly bool _ordinal;

        public LiteralSearch(SearchSettings settings)
        {
            _query = settings.SearchString;
            _compare = CultureInfo.CurrentCulture.CompareInfo;
            var linguisticOptions = settings.MatchCase ? CompareOptions.None : CompareOptions.IgnoreCase;
            // Queries that explicitly name protected characters must match the actual text.
            _ordinal = ContainsProtectedCharacter(_query) || _compare.Compare(_query, "", linguisticOptions) == 0;
            _options = _ordinal ? (settings.MatchCase ? CompareOptions.Ordinal : CompareOptions.OrdinalIgnoreCase) : linguisticOptions;
        }

        public int Find(ReadOnlySpan<char> text, bool forward, out int length) =>
            forward ? _compare.IndexOf(text, _query, _options, out length)
                : _compare.LastIndexOf(text, _query, _options, out length);

        public bool Matches(ReadOnlySpan<char> text, int length) =>
            CanConsume(text, length) && ComparesEqual(text[..length]);

        public bool ValidateBackwardLength(ReadOnlySpan<char> candidate, ref int length)
        {
            if (_ordinal || ComparesEqual(candidate[..length])) return true;

            // A reverse ICU match can report only the first code unit of a contraction.
            // Confirm its length from the same start within the original search window.
            if (!_compare.IsPrefix(candidate, _query, _options, out int verifiedLength) ||
                verifiedLength == 0 || !ComparesEqual(candidate[..verifiedLength])) return false;
            length = verifiedLength;
            return true;
        }

        private bool ComparesEqual(ReadOnlySpan<char> text) => _compare.Compare(text, _query, _options) == 0;

        public bool CanConsume(ReadOnlySpan<char> text, int length)
        {
            if (_ordinal) return true;
            Rune.DecodeFromUtf16(text[length..], out var following, out _);
            return !IsVariationSelector(following) && !ContainsProtectedCharacter(text[..length]);
        }

        private static bool ContainsProtectedCharacter(ReadOnlySpan<char> text)
        {
            foreach (var character in text.EnumerateRunes())
                if (Rune.GetUnicodeCategory(character) is UnicodeCategory.Control or UnicodeCategory.Format ||
                    character.Value == 0x034F || IsVariationSelector(character)) return true;
            return false;
        }

        private static bool IsVariationSelector(Rune character) => character.Value is
            >= 0x180B and <= 0x180D or 0x180F or >= 0xFE00 and <= 0xFE0F or >= 0xE0100 and <= 0xE01EF;
    }

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
