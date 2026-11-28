using Notepad.NeoEdit;
using System;

namespace NotepadAvalonia.Services.Tools;

internal readonly record struct CommentSyntax(string? Line, string? BlockStart = null, string? BlockEnd = null)
{
    internal static CommentSyntax ForLanguage(SyntaxLanguage language, string filename) => language switch
    {
        SyntaxLanguage.CSharp or SyntaxLanguage.JavaScript or SyntaxLanguage.TypeScript or SyntaxLanguage.Go or SyntaxLanguage.C or
            SyntaxLanguage.Cpp or SyntaxLanguage.Rust or SyntaxLanguage.Java or SyntaxLanguage.Kotlin => new("//", "/*", "*/"),
        SyntaxLanguage.Json when filename.EndsWith(".jsonc", StringComparison.OrdinalIgnoreCase) => new("//", "/*", "*/"),
        SyntaxLanguage.Python or SyntaxLanguage.Yaml or SyntaxLanguage.Toml or SyntaxLanguage.Elixir or SyntaxLanguage.Shell or
            SyntaxLanguage.Dockerfile => new("#"),
        SyntaxLanguage.PowerShell => new("#", "<#", "#>"),
        SyntaxLanguage.CMake => new("#", "#[[", "]]"),
        SyntaxLanguage.Css => new(null, "/*", "*/"),
        SyntaxLanguage.Html or SyntaxLanguage.Xml or SyntaxLanguage.Markdown => new(null, "<!--", "-->"),
        SyntaxLanguage.Batch => new("REM"),
        SyntaxLanguage.Ini => new(";"),
        SyntaxLanguage.Sql => new("--", "/*", "*/"),
        SyntaxLanguage.TeX => new("%"),
        _ => default
    };
}
