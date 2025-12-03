using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using AvaloniaNotePad.Models;

namespace AvaloniaNotePad.Services;

/// <summary>
/// Handles Find and Replace operations matching Windows Notepad behavior
/// </summary>
public class FindReplaceService
{
    /// <summary>
    /// Find next occurrence from current position
    /// </summary>
    public FindResult? FindNext(string text, string searchText, int startIndex, FindOptions options)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(searchText))
            return null;

        if (options.UseRegex)
            return FindWithRegex(text, searchText, startIndex, options, false);
            
        return FindWithString(text, searchText, startIndex, options, false);
    }
    
    /// <summary>
    /// Find previous occurrence from current position
    /// </summary>
    public FindResult? FindPrevious(string text, string searchText, int startIndex, FindOptions options)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(searchText))
            return null;

        if (options.UseRegex)
            return FindWithRegex(text, searchText, startIndex, options, true);
            
        return FindWithString(text, searchText, startIndex, options, true);
    }
    
    /// <summary>
    /// Find all occurrences
    /// </summary>
    public List<FindResult> FindAll(string text, string searchText, FindOptions options)
    {
        var results = new List<FindResult>();
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(searchText))
            return results;

        var index = 0;
        while (index < text.Length)
        {
            var result = FindWithString(text, searchText, index, options, false);
            if (result == null) break;
            
            results.Add(result);
            index = result.Index + result.Length;
        }
        
        return results;
    }
    
    /// <summary>
    /// Replace current selection and find next
    /// </summary>
    public (string newText, FindResult? nextResult) ReplaceNext(
        string text, 
        string searchText, 
        string replaceText,
        int selectionStart,
        int selectionLength,
        FindOptions options)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(searchText))
            return (text, null);

        // Verify current selection matches search
        if (selectionLength > 0)
        {
            var selectedText = text.Substring(selectionStart, selectionLength);
            var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            
            if (selectedText.Equals(searchText, comparison) || 
                (options.UseRegex && Regex.IsMatch(selectedText, searchText, GetRegexOptions(options))))
            {
                // Replace
                text = text.Remove(selectionStart, selectionLength);
                text = text.Insert(selectionStart, replaceText);
            }
        }
        
        // Find next
        var nextResult = FindNext(text, searchText, selectionStart + replaceText.Length, options);
        return (text, nextResult);
    }
    
    /// <summary>
    /// Replace all occurrences
    /// </summary>
    public (string newText, int replacementCount) ReplaceAll(
        string text,
        string searchText,
        string replaceText,
        FindOptions options)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(searchText))
            return (text, 0);

        var count = 0;
        
        if (options.UseRegex)
        {
            var regex = new Regex(searchText, GetRegexOptions(options));
            var matches = regex.Matches(text);
            count = matches.Count;
            text = regex.Replace(text, replaceText);
        }
        else
        {
            var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            
            var index = 0;
            while ((index = text.IndexOf(searchText, index, comparison)) != -1)
            {
                if (options.MatchWholeWord && !IsWholeWord(text, index, searchText.Length))
                {
                    index++;
                    continue;
                }
                
                text = text.Remove(index, searchText.Length);
                text = text.Insert(index, replaceText);
                index += replaceText.Length;
                count++;
            }
        }
        
        return (text, count);
    }
    
    private FindResult? FindWithString(string text, string searchText, int startIndex, FindOptions options, bool reverse)
    {
        var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        
        if (reverse)
        {
            // Search backwards from start position
            var searchEnd = Math.Min(startIndex, text.Length);
            for (var i = searchEnd - searchText.Length; i >= 0; i--)
            {
                if (text.Substring(i, searchText.Length).Equals(searchText, comparison))
                {
                    if (options.MatchWholeWord && !IsWholeWord(text, i, searchText.Length))
                        continue;
                        
                    return new FindResult(i, searchText.Length);
                }
            }
        }
        else
        {
            var index = text.IndexOf(searchText, startIndex, comparison);
            while (index != -1)
            {
                if (!options.MatchWholeWord || IsWholeWord(text, index, searchText.Length))
                    return new FindResult(index, searchText.Length);
                    
                index = text.IndexOf(searchText, index + 1, comparison);
            }
        }
        
        // Wrap around search
        if (options.WrapAround)
        {
            if (reverse && startIndex < text.Length)
            {
                return FindWithString(text, searchText, text.Length, 
                    options with { WrapAround = false }, true);
            }
            else if (!reverse && startIndex > 0)
            {
                var result = FindWithString(text, searchText, 0, 
                    options with { WrapAround = false }, false);
                if (result != null && result.Index < startIndex)
                    return result;
            }
        }
        
        return null;
    }
    
    private FindResult? FindWithRegex(string text, string pattern, int startIndex, FindOptions options, bool reverse)
    {
        try
        {
            var regex = new Regex(pattern, GetRegexOptions(options));
            
            if (reverse)
            {
                var matches = regex.Matches(text.Substring(0, startIndex));
                if (matches.Count > 0)
                {
                    var lastMatch = matches[matches.Count - 1];
                    return new FindResult(lastMatch.Index, lastMatch.Length);
                }
                
                if (options.WrapAround)
                {
                    matches = regex.Matches(text.Substring(startIndex));
                    if (matches.Count > 0)
                    {
                        var lastMatch = matches[matches.Count - 1];
                        return new FindResult(startIndex + lastMatch.Index, lastMatch.Length);
                    }
                }
            }
            else
            {
                var match = regex.Match(text, startIndex);
                if (match.Success)
                    return new FindResult(match.Index, match.Length);
                    
                if (options.WrapAround && startIndex > 0)
                {
                    match = regex.Match(text, 0);
                    if (match.Success && match.Index < startIndex)
                        return new FindResult(match.Index, match.Length);
                }
            }
        }
        catch (RegexParseException)
        {
            // Invalid regex pattern
        }
        
        return null;
    }
    
    private bool IsWholeWord(string text, int index, int length)
    {
        var start = index > 0 ? text[index - 1] : ' ';
        var end = index + length < text.Length ? text[index + length] : ' ';
        
        return !char.IsLetterOrDigit(start) && !char.IsLetterOrDigit(end);
    }
    
    private RegexOptions GetRegexOptions(FindOptions options)
    {
        var regexOptions = RegexOptions.None;
        if (!options.MatchCase)
            regexOptions |= RegexOptions.IgnoreCase;
        return regexOptions;
    }
}

/// <summary>
/// Result of a find operation
/// </summary>
public record FindResult(int Index, int Length);

/// <summary>
/// Options for find operations
/// </summary>
public record FindOptions
{
    public bool MatchCase { get; init; } = false;
    public bool MatchWholeWord { get; init; } = false;
    public bool UseRegex { get; init; } = false;
    public bool WrapAround { get; init; } = true;
}
