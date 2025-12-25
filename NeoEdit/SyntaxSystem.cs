using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Notepad.NeoEdit
{
    public enum SyntaxLanguage
    {
        Plain, CSharp, Json, Xml, Python, JavaScript, Html, Sql
    }

    public class SyntaxTheme
    {
        public IBrush Default { get; }
        public IBrush Keyword { get; }   // public, class, if
        public IBrush String { get; }    // "hello"
        public IBrush Comment { get; }   // // comment
        public IBrush Number { get; }    // 123
        public IBrush Type { get; }      // void, int, var
        public IBrush Function { get; }  // Method()

        // Atom One Light Palette
        public SyntaxTheme()
        {
            Default = Brush.Parse("#383A42");
            Keyword = Brush.Parse("#A626A4"); // Purple
            String = Brush.Parse("#50A14F");  // Green
            Comment = Brush.Parse("#A0A1A7"); // Grey
            Number = Brush.Parse("#986801");  // Orange
            Type = Brush.Parse("#C18401");    // Dark Orange
            Function = Brush.Parse("#4078F2");// Blue
        }
    }

    public static class SyntaxMatcher
    {
        private static readonly SyntaxTheme Theme = new();

        // Registry of Rules per Language
        private static readonly Dictionary<SyntaxLanguage, List<(Regex Rule, IBrush Brush)>> Rules = new();

        static SyntaxMatcher()
        {
            // --- C# Rules ---

            var csharp = new List<(Regex, IBrush)>();

            // 1. Comments (Grey) - Priority 1 (Match these first so they don't get colored as keywords)
            csharp.Add((new Regex(@"//.*$", RegexOptions.Compiled), Theme.Comment));

            // 2. Strings (Green)
            csharp.Add((new Regex(@""".*?""", RegexOptions.Compiled), Theme.String));

            // 3. Keywords (Purple) - Control flow, modifiers
            csharp.Add((new Regex(@"\b(public|private|protected|internal|class|struct|enum|interface|return|if|else|for|foreach|while|switch|case|break|continue|using|namespace|static|readonly|async|await|new)\b", RegexOptions.Compiled), Theme.Keyword));

            // 4. Primitive Types (Orange/Gold) - MOVED HERE
            csharp.Add((new Regex(@"\b(void|int|string|bool|double|float|var|object|char|byte|long|decimal)\b", RegexOptions.Compiled), Theme.Type));

            // 5. Likely Class Names (Orange/Gold) - Heuristic: Starts with Uppercase, usually a Type
            // This makes "Console", "Math", "HelloWorld" orange.
            csharp.Add((new Regex(@"\b[A-Z]\w+\b", RegexOptions.Compiled), Theme.Type));

            // 6. Numbers
            csharp.Add((new Regex(@"\b\d+\b", RegexOptions.Compiled), Theme.Number));

            Rules[SyntaxLanguage.CSharp] = csharp;

            // --- JSON Rules ---
            var json = new List<(Regex, IBrush)>();
            json.Add((new Regex(@""".*?""(?=\s*:)", RegexOptions.Compiled), Theme.Keyword)); // Keys
            json.Add((new Regex(@":\s*("".*?"")", RegexOptions.Compiled), Theme.String));    // Values
            json.Add((new Regex(@"\b(true|false|null)\b", RegexOptions.Compiled), Theme.Number));
            json.Add((new Regex(@"\b\d+(\.\d+)?\b", RegexOptions.Compiled), Theme.Number));
            Rules[SyntaxLanguage.Json] = json;

            // --- Python Rules ---
            var python = new List<(Regex, IBrush)>();
            python.Add((new Regex(@"#.*$", RegexOptions.Compiled), Theme.Comment));
            python.Add((new Regex(@""".*?""|'.*?'", RegexOptions.Compiled), Theme.String));
            python.Add((new Regex(@"\b(def|class|if|elif|else|while|for|in|return|import|from|as|try|except|finally|with|lambda|pass|break|continue|True|False|None)\b", RegexOptions.Compiled), Theme.Keyword));
            python.Add((new Regex(@"\b\d+\b", RegexOptions.Compiled), Theme.Number));
            Rules[SyntaxLanguage.Python] = python;

            // --- XML/HTML Rules ---
            var xml = new List<(Regex, IBrush)>();
            xml.Add((new Regex(@"<!--[\s\S]*?-->", RegexOptions.Compiled), Theme.Comment));
            xml.Add((new Regex(@"</?\w+", RegexOptions.Compiled), Theme.Keyword)); // Tags
            xml.Add((new Regex(@""".*?""", RegexOptions.Compiled), Theme.String)); // Attributes
            xml.Add((new Regex(@">", RegexOptions.Compiled), Theme.Keyword));
            Rules[SyntaxLanguage.Xml] = xml;
            Rules[SyntaxLanguage.Html] = xml;

            // --- JavaScript Rules ---
            var js = new List<(Regex, IBrush)>();
            js.Add((new Regex(@"//.*$", RegexOptions.Compiled), Theme.Comment));
            js.Add((new Regex(@""".*?""|'.*?'|`.*?`", RegexOptions.Compiled), Theme.String));
            js.Add((new Regex(@"\b(function|const|let|var|if|else|for|while|return|import|export|default|class|extends|new|this|async|await)\b", RegexOptions.Compiled), Theme.Keyword));
            js.Add((new Regex(@"\b\d+\b", RegexOptions.Compiled), Theme.Number));
            Rules[SyntaxLanguage.JavaScript] = js;
        }

        public static SyntaxLanguage DetectLanguage(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return SyntaxLanguage.Plain;
            string ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();

            return ext switch
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
        }

        // NEW METHOD: Returns a list of styles instead of modifying a layout
        public static List<ValueSpan<TextRunProperties>>? GetOverrides(string text, SyntaxLanguage lang, Typeface typeface, double fontSize, IBrush defaultForeground)
        {
            if (lang == SyntaxLanguage.Plain || !Rules.ContainsKey(lang)) return null;

            var overrides = new List<ValueSpan<TextRunProperties>>();

            // We need a base set of properties to clone with different colors
            var baseProps = new GenericTextRunProperties(typeface, fontSize, null, defaultForeground);

            foreach (var (rule, brush) in Rules[lang])
            {
                foreach (Match m in rule.Matches(text))
                {
                    // Create a specific property for this match (e.g., Green Brush)
                    var highlightProps = new GenericTextRunProperties(typeface, fontSize, null, brush);

                    // Add the span (Index, Length, Properties)
                    overrides.Add(new ValueSpan<TextRunProperties>(m.Index, m.Length, highlightProps));
                }
            }

            // Optimziation: TextLayout prefers sorted spans, though mostly handles unsorted.
            // If overlapping spans occur, later ones usually win or it gets complex. 
            // For this simple regex engine, just returning the list is usually fine.
            return overrides;
        }
    }
}