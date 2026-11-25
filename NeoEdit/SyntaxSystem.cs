using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Notepad.NeoEdit;

public enum SyntaxLanguage
{
    Plain, CSharp, Json, Xml, Python, JavaScript, Html, Sql
}

internal static class SyntaxMatcher
{
    private enum Token { Keyword, String, Comment, Number, Type }
    private static readonly IBrush[] Light = new[] { "#A626A4", "#40791E", "#626875", "#986801", "#925A05" }.Select(Brush.Parse).ToArray();
    private static readonly IBrush[] Dark = new[] { "#DDA5F2", "#9DDC80", "#A3AAB6", "#E6B572", "#F4C591" }.Select(Brush.Parse).ToArray();

    private const string DoubleQuoted = "\\x22(?:\\\\.|[^\\x22\\\\])*\\x22";
    private const string SingleQuoted = @"'(?:\\.|[^'\\])*'";
    private const string Number = @"\b\d+(?:\.\d+)?\b";

    private sealed record Highlighter(Regex Pattern, Token[] Tokens);

    private static readonly Highlighter Xml = Build(
        (Token.Comment, @"<!--.*?-->"), (Token.Keyword, @"</?[\w:.-]+|/?>"),
        (Token.String, DoubleQuoted + "|" + SingleQuoted));
    private static readonly Dictionary<SyntaxLanguage, Highlighter> Rules = new()
    {
        [SyntaxLanguage.CSharp] = Build(
            (Token.Comment, "//.*$"),
            (Token.String, DoubleQuoted + "|" + SingleQuoted),
            (Token.Keyword, Words("public|private|protected|internal|class|struct|enum|interface|return|if|else|for|foreach|while|switch|case|break|continue|using|namespace|static|readonly|async|await|new")),
            (Token.Type, Words("void|int|string|bool|double|float|var|object|char|byte|long|decimal") + @"|\b[A-Z]\w+\b"),
            (Token.Number, Number)),
        [SyntaxLanguage.Json] = Build(
            (Token.Keyword, DoubleQuoted + @"(?=\s*:)"),
            (Token.String, DoubleQuoted), (Token.Number, Number + "|" + Words("true|false|null"))),
        [SyntaxLanguage.Python] = Build(
            (Token.Comment, "#.*$"), (Token.String, DoubleQuoted + "|" + SingleQuoted),
            (Token.Keyword, Words("def|class|if|elif|else|while|for|in|return|import|from|as|try|except|finally|with|lambda|pass|break|continue|True|False|None")),
            (Token.Number, Number)),
        [SyntaxLanguage.JavaScript] = Build(
            (Token.Comment, "//.*$"), (Token.String, DoubleQuoted + "|" + SingleQuoted + @"|\x60(?:\\.|[^\\\x60])*\x60"),
            (Token.Keyword, Words("function|const|let|var|if|else|for|while|return|import|export|default|class|extends|new|this|async|await")),
            (Token.Number, Number)),
        [SyntaxLanguage.Xml] = Xml,
        [SyntaxLanguage.Html] = Xml,
        [SyntaxLanguage.Sql] = Build(
            (Token.Comment, @"--.*$|/\*.*?\*/"),
            (Token.String, @"'(?:''|[^'])*'"),
            (Token.Keyword, "(?i:" + Words("select|from|where|as|and|or|not|join|on|distinct|union|all|insert|into|values|update|set|delete|create|alter|drop|table|group|by|order|having|count|case|when|then|else|end|null|limit|offset") + ")"),
            (Token.Number, Number))
    };

    private static string Words(string alternatives) => @"\b(?:" + alternatives + @")\b";

    private static Highlighter Build(params (Token Token, string Pattern)[] rules)
    {
        var regex = new Regex(string.Join("|", rules.Select(rule => $"(?<{rule.Token}>{rule.Pattern})")),
            RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(75));
        return new(regex, regex.GetGroupNames().Skip(1).Select(Enum.Parse<Token>).ToArray());
    }

    public static SyntaxLanguage DetectLanguage(string filePath) =>
        Path.GetExtension(filePath)?.ToLowerInvariant() switch
        {
            ".cs" => SyntaxLanguage.CSharp,
            ".json" => SyntaxLanguage.Json,
            ".xml" or ".xaml" or ".csproj" => SyntaxLanguage.Xml,
            ".html" or ".htm" => SyntaxLanguage.Html,
            ".js" or ".ts" => SyntaxLanguage.JavaScript,
            ".py" => SyntaxLanguage.Python,
            ".sql" => SyntaxLanguage.Sql,
            _ => SyntaxLanguage.Plain
        };

    public static List<ValueSpan<TextRunProperties>>? GetOverrides(string text, SyntaxLanguage language,
        Typeface typeface, double fontSize, bool dark)
    {
        if (!Rules.TryGetValue(language, out var highlighter)) return null;

        var styles = new List<ValueSpan<TextRunProperties>>();
        var palette = dark ? Dark : Light;
        var properties = new TextRunProperties?[palette.Length];
        try
        {
            foreach (Match match in highlighter.Pattern.Matches(text))
            {
                for (int index = 0; index < highlighter.Tokens.Length; index++)
                {
                    if (!match.Groups[index + 1].Success) continue;
                    int token = (int)highlighter.Tokens[index];
                    var style = properties[token] ??= new GenericTextRunProperties(typeface, fontSize, null, palette[token]);
                    styles.Add(new ValueSpan<TextRunProperties>(match.Index, match.Length, style));
                    break;
                }
            }
        }
        catch (RegexMatchTimeoutException)
        {
            // Let an unusually large or malformed line render with the styles already found.
        }
        return styles.Count == 0 ? null : styles;
    }
}
