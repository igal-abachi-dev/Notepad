using Notepad.NeoEdit;
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

        int startPos = forward ? editor.CaretOffset : Math.Max(0, editor.CaretOffset - 1);

        bool wrapped = false;

        if (settings.UseRegex)
        {
            var options = settings.MatchCase
                ? RegexOptions.None
                : RegexOptions.IgnoreCase;
            var regex = new Regex(searchText, options);

            if (forward)
            {
                var match = regex.Match(text, startPos);
                if (settings.WrapAround && !match.Success && text.Length > 0)
                {
                    wrapped = true;
                    match = regex.Match(text, 0);
                }
                if (match.Success) return (new SearchResult(match.Index, match.Length), wrapped);
            }
            else
            {
                var matches = regex.Matches(text[..Math.Min(startPos, text.Length)]);
                if (matches.Count > 0)
                {
                    var last = matches[^1];
                    return (new SearchResult(last.Index, last.Length), wrapped);
                }
                if (settings.WrapAround && text.Length > 0)
                {
                    wrapped = true;
                    matches = regex.Matches(text);
                    if (matches.Count > 0)
                    {
                        var last = matches[^1];
                        return (new SearchResult(last.Index, last.Length), wrapped);
                    }
                }
            }
        }
        else
        {
            var comparison = settings.MatchCase
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;
            int foundPos;

            if (forward)
            {
                foundPos = FindForward(text, searchText, startPos, comparison, settings.WholeWord);
                if (foundPos == -1 && settings.WrapAround && text.Length > 0)
                {
                    wrapped = true;
                    foundPos = FindForward(text, searchText, 0, comparison, settings.WholeWord);
                }
            }
            else
            {
                foundPos = FindBackward(text, searchText, startPos, comparison, settings.WholeWord);
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
        var (result, _) = FindNext(editor, settings);
        if (result != null)
        {
            editor.ReplaceRange(result.StartOffset, result.Length, settings.ReplaceString);
            return 1;
        }
        return 0;
    }



    public int ReplaceAll(NeoEditor editor, SearchSettings settings)
    {
        if (string.IsNullOrEmpty(settings.SearchString)) return 0;

        string text = editor.GetText();
        string newText;
        int count = 0;

        if (settings.UseRegex)
        {
            var options = settings.MatchCase ? RegexOptions.None : RegexOptions.IgnoreCase;
            var regex = new Regex(settings.SearchString, options);
            count = regex.Matches(text).Count;
            newText = regex.Replace(text, settings.ReplaceString);
        }
        else
        {
            // Simple replace all
            if (settings.MatchCase)
            {
                count = (text.Length - text.Replace(settings.SearchString, "").Length) / settings.SearchString.Length;
                newText = text.Replace(settings.SearchString, settings.ReplaceString);
            }
            else
            {
                // Case-insensitive replace is trickier, standard Replace(str, str) is case sensitive
                // Using Regex for convenience here
                var regex = new Regex(Regex.Escape(settings.SearchString), RegexOptions.IgnoreCase);
                count = regex.Matches(text).Count;
                newText = regex.Replace(text, settings.ReplaceString);
            }
        }

        if (count > 0)
        {
            editor.Text = newText; // Triggers full reload, safest for ReplaceAll
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
