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
    Plain, CSharp, Json, Xml, Python, JavaScript, Html, Sql, PowerShell, Batch,
    TypeScript, Go, C, Cpp, Rust, Java, Kotlin, Elixir, Css, Markdown, Dockerfile, CMake, Shell, TeX,
    Ini, Toml, Yaml, Log
}

internal static partial class SyntaxMatcher
{
    private enum Token { Keyword, String, Comment, Number, Type }
    private static readonly IBrush[] Light = new[] { "#A626A4", "#40791E", "#626875", "#986801", "#925A05" }.Select(Brush.Parse).ToArray();
    private static readonly IBrush[] Dark = new[] { "#DDA5F2", "#9DDC80", "#A3AAB6", "#E6B572", "#F4C591" }.Select(Brush.Parse).ToArray();

    private const string DoubleQuoted = "\\x22(?:\\\\.|[^\\x22\\\\])*\\x22";
    private const string SingleQuoted = @"'(?:\\.|[^'\\])*'";
    private const string Number = @"\b\d+(?:\.\d+)?\b";

    private sealed record Highlighter(Regex Pattern, Token[] Tokens);

    private static readonly Lazy<Highlighter> CoreXml = new(() => Build(
        (Token.Comment, @"<!--.*?-->"), (Token.Keyword, @"</?[\w:.-]+|/?>"),
        (Token.String, DoubleQuoted + "|" + SingleQuoted)));
    private static Highlighter? GetCoreRules(SyntaxLanguage language) => language switch
    {
        SyntaxLanguage.CSharp => CoreCSharp.Value,
        SyntaxLanguage.Json => CoreJson.Value,
        SyntaxLanguage.Python => CorePython.Value,
        SyntaxLanguage.JavaScript => CoreJavaScript.Value,
        SyntaxLanguage.Xml or SyntaxLanguage.Html => CoreXml.Value,
        SyntaxLanguage.PowerShell => CorePowerShell.Value,
        SyntaxLanguage.Batch => CoreBatch.Value,
        SyntaxLanguage.Sql => CoreSql.Value,
        _ => null
    };

    private static readonly Lazy<Highlighter> CoreCSharp = new(() => Build(
            (Token.Comment, "//.*$"),
            (Token.String, DoubleQuoted + "|" + SingleQuoted),
            (Token.Keyword, Words("public|private|protected|internal|class|struct|enum|interface|return|if|else|for|foreach|while|switch|case|break|continue|using|namespace|static|readonly|async|await|new")),
            (Token.Type, Words("void|int|string|bool|double|float|var|object|char|byte|long|decimal") + @"|\b[A-Z]\w+\b"),
            (Token.Number, Number)));
    private static readonly Lazy<Highlighter> CoreJson = new(() => Build(
            (Token.Comment, @"//.*$|/\*.*?\*/"),
            (Token.Keyword, DoubleQuoted + @"(?=\s*:)"),
            (Token.String, DoubleQuoted), (Token.Number, Number + "|" + Words("true|false|null"))));
    private static readonly Lazy<Highlighter> CorePython = new(() => Build(
            (Token.Comment, "#.*$"), (Token.String, DoubleQuoted + "|" + SingleQuoted),
            (Token.Keyword, Words("def|class|if|elif|else|while|for|in|return|import|from|as|try|except|finally|with|lambda|pass|break|continue|True|False|None")),
            (Token.Number, Number)));
    private static readonly Lazy<Highlighter> CoreJavaScript = new(() => Build(
            (Token.Comment, "//.*$"), (Token.String, DoubleQuoted + "|" + SingleQuoted + @"|\x60(?:\\.|[^\\\x60])*\x60"),
            (Token.Keyword, Words("function|const|let|var|if|else|for|while|return|import|export|default|class|extends|new|this|async|await")),
            (Token.Number, Number)));
    private static readonly Lazy<Highlighter> CorePowerShell = new(() => Build(
            (Token.Comment, "#.*$"), (Token.String, DoubleQuoted + "|" + SingleQuoted),
            (Token.Keyword, "(?i:" + Words("param|function|filter|if|elseif|else|switch|foreach|for|while|do|until|return|break|continue|try|catch|finally|throw|class|enum|using|begin|process|end") + ")"),
            (Token.Type, @"\$(?:[a-zA-Z_]\w*(?::[a-zA-Z_]\w*)?)|\b[a-zA-Z]+-[a-zA-Z]\w*\b"),
            (Token.Number, Number)));
    private static readonly Lazy<Highlighter> CoreBatch = new(() => Build(
            (Token.Comment, @"(?i:^\s*(?:rem\b|::).*$)"), (Token.String, DoubleQuoted),
            (Token.Keyword, "(?i:" + Words("echo|set|setlocal|endlocal|if|else|for|in|do|goto|call|exit|pause|shift|start") + ")"),
            (Token.Type, @"%[a-zA-Z_]\w*%|%~?\d|%%[a-zA-Z]"), (Token.Number, Number)));
    private static readonly Lazy<Highlighter> CoreSql = new(() => Build(
            (Token.Comment, @"--.*$|/\*.*?\*/"),
            (Token.String, @"'(?:''|[^'])*'"),
            (Token.Keyword, "(?i:" + Words("select|from|where|as|and|or|not|join|on|distinct|union|all|insert|into|values|update|set|delete|create|alter|drop|table|group|by|order|having|count|case|when|then|else|end|null|limit|offset") + ")"),
            (Token.Number, Number)));

    private static string Words(string alternatives) => @"\b(?:" + alternatives + @")\b";

    private static Highlighter Build(params (Token Token, string Pattern)[] rules)
    {
        var regex = new Regex(string.Join("|", rules.Select(rule => $"(?<{rule.Token}>{rule.Pattern})")),
            RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(75));
        return new(regex, regex.GetGroupNames().Skip(1).Select(Enum.Parse<Token>).ToArray());
    }

    public static SyntaxLanguage DetectLanguage(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath) || filePath.Contains('\0')) return SyntaxLanguage.Plain;
        try
        {
            string name = Path.GetFileName(filePath).ToLowerInvariant();
            if (name is "cmakelists.txt") return SyntaxLanguage.CMake;
            if (name is "dockerfile" or "containerfile" || name.StartsWith("dockerfile.", StringComparison.Ordinal) ||
                name.StartsWith("containerfile.", StringComparison.Ordinal)) return SyntaxLanguage.Dockerfile;
            if (name is "cargo.lock" or "poetry.lock" or "pipfile") return SyntaxLanguage.Toml;
            if (name is ".bashrc" or ".bash_profile" or ".zshrc" or ".zprofile" or ".profile") return SyntaxLanguage.Shell;
            if (name is "go.mod" or "go.sum") return SyntaxLanguage.Go;
            string extension = Path.GetExtension(name);
            if (extension.Length > 1 && extension.AsSpan(1).IndexOfAnyExceptInRange('0', '9') < 0 &&
                Path.GetFileNameWithoutExtension(name).EndsWith(".log", StringComparison.Ordinal)) return SyntaxLanguage.Log;
            return extension switch
            {
                ".cs" or ".csx" => SyntaxLanguage.CSharp,
                ".json" or ".jsonc" or ".jsonl" or ".ndjson" or ".geojson" => SyntaxLanguage.Json,
                ".xml" or ".xaml" or ".axaml" or ".csproj" or ".fsproj" or ".vbproj" or ".vcxproj" or
                    ".props" or ".targets" or ".slnx" or ".svg" or ".resx" or ".nuspec" or ".config" => SyntaxLanguage.Xml,
                ".html" or ".htm" or ".xhtml" or ".vue" or ".svelte" or ".heex" => SyntaxLanguage.Html,
                ".js" or ".mjs" or ".cjs" or ".jsx" => SyntaxLanguage.JavaScript,
                ".ts" or ".mts" or ".cts" or ".tsx" => SyntaxLanguage.TypeScript,
                ".py" or ".pyw" or ".pyi" => SyntaxLanguage.Python,
                ".sql" => SyntaxLanguage.Sql,
                ".ps1" or ".psm1" or ".psd1" => SyntaxLanguage.PowerShell,
                ".cmd" or ".bat" => SyntaxLanguage.Batch,
                ".go" => SyntaxLanguage.Go,
                ".c" => SyntaxLanguage.C,
                ".h" or ".cc" or ".cpp" or ".cxx" or ".hpp" or ".hh" or ".hxx" or ".ixx" or ".inl" => SyntaxLanguage.Cpp,
                ".rs" => SyntaxLanguage.Rust,
                ".java" => SyntaxLanguage.Java,
                ".kt" or ".kts" => SyntaxLanguage.Kotlin,
                ".ex" or ".exs" => SyntaxLanguage.Elixir,
                ".css" => SyntaxLanguage.Css,
                ".md" or ".markdown" or ".mdown" or ".mkd" => SyntaxLanguage.Markdown,
                ".dockerfile" or ".containerfile" => SyntaxLanguage.Dockerfile,
                ".cmake" => SyntaxLanguage.CMake,
                ".sh" or ".bash" or ".zsh" or ".ksh" => SyntaxLanguage.Shell,
                ".tex" or ".ltx" or ".sty" or ".cls" or ".bib" => SyntaxLanguage.TeX,
                ".ini" or ".cfg" or ".conf" or ".properties" or ".editorconfig" or ".gitconfig" => SyntaxLanguage.Ini,
                ".toml" => SyntaxLanguage.Toml,
                ".yml" or ".yaml" => SyntaxLanguage.Yaml,
                ".log" or ".trace" => SyntaxLanguage.Log,
                _ => SyntaxLanguage.Plain
            };
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return SyntaxLanguage.Plain;
        }
    }

    internal static SyntaxLanguage DetectUntitledLanguage(ReadOnlySpan<char> prefix)
    {
        while (!prefix.IsEmpty && (char.IsWhiteSpace(prefix[0]) || prefix[0] == '\uFEFF')) prefix = prefix[1..];
        if (prefix.IsEmpty) return SyntaxLanguage.Plain;
        if (prefix[0] == '{') return SyntaxLanguage.Json;
        if (prefix[0] != '<') return SyntaxLanguage.Plain;
        if (prefix.StartsWith("<!doctype html", StringComparison.OrdinalIgnoreCase) ||
            prefix.StartsWith("<html", StringComparison.OrdinalIgnoreCase) &&
                (prefix.Length == 5 || char.IsWhiteSpace(prefix[5]) || prefix[5] == '>')) return SyntaxLanguage.Html;
        return prefix.Length == 1 || char.IsLetter(prefix[1]) || prefix[1] is '?' or '!' or '_'
            ? SyntaxLanguage.Xml : SyntaxLanguage.Plain;
    }

    public static List<ValueSpan<TextRunProperties>>? GetOverrides(string text, SyntaxLanguage language,
        Typeface typeface, FontFeatureCollection fontFeatures, double fontSize, bool dark)
    {
        var highlighter = GetCoreRules(language) ?? GetExtraRules(language);
        if (highlighter == null) return null;

        var styles = new List<ValueSpan<TextRunProperties>>();
        var palette = dark ? Dark : Light;
        var properties = new TextRunProperties?[palette.Length];
        int lastToken = -1;
        try
        {
            foreach (Match match in highlighter.Pattern.Matches(text))
            {
                for (int index = 0; index < highlighter.Tokens.Length; index++)
                {
                    if (!match.Groups[index + 1].Success) continue;
                    int token = (int)highlighter.Tokens[index];
                    var style = properties[token] ??= new GenericTextRunProperties(typeface, fontFeatures, fontSize,
                        foregroundBrush: palette[token]);
                    if (lastToken == token && styles[^1] is { } previous && previous.Start + previous.Length == match.Index)
                        styles[^1] = new ValueSpan<TextRunProperties>(previous.Start, previous.Length + match.Length, style);
                    else
                        styles.Add(new ValueSpan<TextRunProperties>(match.Index, match.Length, style));
                    lastToken = token;
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
