using Avalonia.Media;
using Notepad.NeoEdit;
using System.Linq;
using Xunit;

namespace Notepad.Tests;

public class SyntaxMatcherTests
{
    [Theory]
    [InlineData("settings.INI", SyntaxLanguage.Ini, "[server]")]
    [InlineData(".editorconfig", SyntaxLanguage.Ini, "indent_size = 4")]
    [InlineData("Cargo.lock", SyntaxLanguage.Toml, "[[package]]")]
    [InlineData("compose.yml", SyntaxLanguage.Yaml, "  image: app")]
    [InlineData("README.md", SyntaxLanguage.Markdown, "# Getting started")]
    [InlineData("app.log.1", SyntaxLanguage.Log, "2026-11-28T12:01:03.123Z INFO 192.168.1.1")]
    [InlineData("sample.csx", SyntaxLanguage.CSharp, "public int count = 1;")]
    [InlineData("sample.pyi", SyntaxLanguage.Python, "def run() -> None:")]
    [InlineData("worker.mjs", SyntaxLanguage.JavaScript, "const fn = x => x;")]
    [InlineData("sample.tsx", SyntaxLanguage.TypeScript, "interface Options { value: string }")]
    [InlineData("main.go", SyntaxLanguage.Go, "package main")]
    [InlineData("main.c", SyntaxLanguage.C, "int main(void)")]
    [InlineData("api.hpp", SyntaxLanguage.Cpp, "template<class T>")]
    [InlineData("lib.rs", SyntaxLanguage.Rust, "pub fn run() -> usize {")]
    [InlineData("Main.java", SyntaxLanguage.Java, "public record Item(int id) {}")]
    [InlineData("build.gradle.kts", SyntaxLanguage.Kotlin, "val name = 1")]
    [InlineData("app.exs", SyntaxLanguage.Elixir, "defmodule App do")]
    [InlineData("site.xhtml", SyntaxLanguage.Html, "<p class=\"entry\">")]
    [InlineData("Editor.axaml", SyntaxLanguage.Xml, "<Window Width=\"100\">")]
    [InlineData("site.css", SyntaxLanguage.Css, "color: #123456;")]
    [InlineData("query.sql", SyntaxLanguage.Sql, "select 1")]
    [InlineData("Dockerfile.dev", SyntaxLanguage.Dockerfile, "FROM debian:12")]
    [InlineData("CMakeLists.txt", SyntaxLanguage.CMake, "add_library(app STATIC app.c)")]
    [InlineData(".zshrc", SyntaxLanguage.Shell, "export TMPDIR=\"/tmp\"")]
    [InlineData("setup.psm1", SyntaxLanguage.PowerShell, "$name = Get-Date")]
    [InlineData("build.cmd", SyntaxLanguage.Batch, "echo %PATH%")]
    [InlineData("paper.tex", SyntaxLanguage.TeX, "\\section{Intro}")]
    public void LanguageDetection_UsesFilenameAndBuildsRulesForThatLanguage(string path, SyntaxLanguage language, string line)
    {
        Assert.Equal(language, SyntaxMatcher.DetectLanguage(path));
        Assert.NotEmpty(SyntaxMatcher.GetOverrides(line, language, Typeface.Default, EditorFontFeatures.ForLigatures(true), 16, dark: false)!);
    }

    [Fact]
    public void LogHighlighting_SeparatesTimestampSeverityAddressesAndStackFrames()
    {
        var features = EditorFontFeatures.ForLigatures(true);
        const string line = "2026-11-28T12:01:03.123Z INFO WARN ERROR 192.168.1.1 2001:db8::1";
        var spans = SyntaxMatcher.GetOverrides(line, SyntaxLanguage.Log, Typeface.Default, features, 16, dark: false)!;
        Assert.Equal(new[] { "2026-11-28T12:01:03.123Z", "INFO", "WARN", "ERROR", "192.168.1.1", "2001:db8::1" },
            spans.Select(span => line.Substring(span.Start, span.Length)));
        Assert.Equal(3, spans.Skip(1).Take(3).Select(span => span.Value.ForegroundBrush).Distinct().Count());
        const string frame = "  at App.Server.Run() in Server.cs:line 15";
        Assert.Equal(frame.Length, Assert.Single(SyntaxMatcher.GetOverrides(frame, SyntaxLanguage.Log,
            Typeface.Default, features, 16, dark: false)!).Length);
    }

    [Fact]
    public void StyleRuns_MergeIdenticalNeighborsAndShareTheBaseShapingFeatures()
    {
        var features = EditorFontFeatures.ForLigatures(false);
        var spans = SyntaxMatcher.GetOverrides("<a>text</a><b p=\"1\"/>", SyntaxLanguage.Xml, Typeface.Default,
            features, 16, dark: false)!;
        Assert.Collection(spans,
            span => Assert.Equal((0, 3), (span.Start, span.Length)),
            span => Assert.Equal((7, 6), (span.Start, span.Length)),
            span => Assert.Equal((16, 3), (span.Start, span.Length)),
            span => Assert.Equal((19, 2), (span.Start, span.Length)));
        Assert.All(spans, span => Assert.Same(features, span.Value.FontFeatures));
        Assert.NotSame(spans[2].Value.ForegroundBrush, spans[3].Value.ForegroundBrush);
    }
}
