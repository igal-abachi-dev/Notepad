using System;

namespace Notepad.NeoEdit;

internal static partial class SyntaxMatcher
{
    private const string SlashComment = @"//.*$|/\*.*?\*/";
    private const string Quoted = DoubleQuoted + "|" + SingleQuoted;
    private const string Backtick = @"\x60[^\x60]*\x60";
    private const string ScriptVariable = @"\$(?:\{[^}]*\}|[A-Za-z_]\w*|\d)";

    private static Highlighter? GetExtraRules(SyntaxLanguage language) => language switch
    {
        SyntaxLanguage.TypeScript => TypeScript.Value,
        SyntaxLanguage.Go => Go.Value,
        SyntaxLanguage.C => C.Value,
        SyntaxLanguage.Cpp => Cpp.Value,
        SyntaxLanguage.Rust => Rust.Value,
        SyntaxLanguage.Java => Java.Value,
        SyntaxLanguage.Kotlin => Kotlin.Value,
        SyntaxLanguage.Elixir => Elixir.Value,
        SyntaxLanguage.Css => Css.Value,
        SyntaxLanguage.Markdown => Markdown.Value,
        SyntaxLanguage.Dockerfile => Dockerfile.Value,
        SyntaxLanguage.CMake => CMake.Value,
        SyntaxLanguage.Shell => Shell.Value,
        SyntaxLanguage.TeX => TeX.Value,
        SyntaxLanguage.Ini => Ini.Value,
        SyntaxLanguage.Toml => Toml.Value,
        SyntaxLanguage.Yaml => Yaml.Value,
        SyntaxLanguage.Log => Log.Value,
        _ => null
    };

    private static readonly Lazy<Highlighter> TypeScript = new(() => Build(
        (Token.Comment, SlashComment), (Token.String, Quoted + "|" + Backtick),
        (Token.Keyword, Words("class|const|let|var|function|return|if|else|switch|case|for|while|new|this|import|export|default|async|await|extends|implements|interface|type|enum|namespace|declare|as|satisfies|keyof|infer|readonly|public|private|protected|abstract|override")),
        (Token.Type, Words("string|number|boolean|bigint|symbol|object|unknown|never|any|void")),
        (Token.Number, Number + "|" + Words("true|false|null|undefined"))));

    private static readonly Lazy<Highlighter> Go = new(() => Build(
        (Token.Comment, SlashComment), (Token.String, Quoted + "|" + Backtick),
        (Token.Keyword, Words("package|import|func|type|struct|interface|map|chan|select|case|default|const|var|go|defer|range|for|if|else|switch|return|break|continue|goto|fallthrough|module|require|replace")),
        (Token.Type, Words("bool|byte|rune|string|int|int32|int64|uint|uint32|uint64|uintptr|float32|float64|complex64|complex128|error|any|comparable")),
        (Token.Number, Number + "|" + Words("true|false|nil"))));

    private const string CKeywords = "auto|break|case|const|continue|default|do|else|enum|extern|for|goto|if|inline|register|restrict|return|sizeof|static|struct|switch|typedef|union|volatile|while|_Alignas|_Static_assert";
    private const string CTypes = "void|char|short|int|long|float|double|signed|unsigned|bool|size_t|ptrdiff_t";
    private static readonly Lazy<Highlighter> C = new(() => CLike(CKeywords, CTypes, @"^[ \t]*#[ \t]*\w+"));
    private static readonly Lazy<Highlighter> Cpp = new(() => CLike(CKeywords +
        "|class|namespace|using|public|private|protected|template|typename|virtual|override|final|constexpr|consteval|constinit|noexcept|new|delete|this|try|catch|throw|decltype|concept|requires|operator|explicit|export|module|import|co_await|co_return|co_yield",
        CTypes + "|wchar_t|char8_t|char16_t|char32_t", @"^[ \t]*#[ \t]*\w+"));
    private static readonly Lazy<Highlighter> Java = new(() => CLike(
        "abstract|assert|break|case|catch|class|continue|default|do|else|enum|extends|final|finally|for|if|implements|import|instanceof|interface|native|new|package|private|protected|public|record|return|sealed|static|super|switch|synchronized|this|throw|throws|try|var|void|volatile|while|yield",
        "boolean|byte|char|short|int|long|float|double", @"@[A-Za-z_]\w*"));
    private static readonly Lazy<Highlighter> Kotlin = new(() => CLike(
        "as|break|class|companion|continue|data|do|else|enum|fun|if|in|interface|is|object|open|package|private|protected|public|return|sealed|super|suspend|this|throw|try|typealias|val|var|when|while|where|import|inline|override",
        "Boolean|Byte|Char|Short|Int|Long|Float|Double|String|Unit|Any|Nothing", @"@[A-Za-z_]\w*"));

    private static Highlighter CLike(string keywords, string types, string special) => Build(
        (Token.Comment, SlashComment), (Token.String, Quoted), (Token.Type, special),
        (Token.Keyword, Words(keywords)), (Token.Type, Words(types)),
        (Token.Number, @"\b0[xX][0-9A-Fa-f]+\b|" + Number + "|" + Words("true|false|null|nullptr|NULL")));

    private static readonly Lazy<Highlighter> Rust = new(() => Build(
        (Token.Comment, SlashComment), (Token.String, DoubleQuoted + @"|'(?:\\.|[^'\\])'"),
        (Token.Keyword, Words("as|async|await|break|const|continue|crate|dyn|else|enum|extern|fn|for|if|impl|in|let|loop|match|mod|move|mut|pub|ref|return|self|Self|static|struct|super|trait|type|unsafe|use|where|while")),
        (Token.Type, @"\b[A-Za-z_]\w*(?=!)|'[A-Za-z_]\w*|" + Words("bool|char|str|u8|u16|u32|u64|u128|usize|i8|i16|i32|i64|i128|isize|f32|f64")),
        (Token.Number, Number + "|" + Words("true|false"))));

    private static readonly Lazy<Highlighter> Elixir = new(() => Build(
        (Token.Comment, "#.*$"), (Token.String, Quoted),
        (Token.Keyword, Words("def|defp|defmodule|defmacro|defprotocol|defimpl|do|end|fn|case|cond|if|unless|with|for|receive|try|rescue|catch|after|else|when|and|or|not|import|alias|require|use")),
        (Token.Type, @"(?<!\w):[A-Za-z_]\w*[!?]?|@[A-Za-z_]\w*|\b[A-Z]\w*\b"),
        (Token.Number, Number + "|" + Words("true|false|nil"))));

    private static readonly Lazy<Highlighter> Css = new(() => Build(
        (Token.Comment, @"/\*.*?\*/"), (Token.String, Quoted),
        (Token.Keyword, @"@[\w-]+|![ \t]*important\b|--[\w-]+|(?<![\w-])[A-Za-z][\w-]*(?=[ \t]*:)"),
        (Token.Number, @"#[0-9A-Fa-f]{3,8}\b|(?:\d*\.)?\d+(?:%|[A-Za-z]+)?")));

    private static readonly Lazy<Highlighter> Markdown = new(() => Build(
        (Token.Comment, @"<!--.*?-->"),
        (Token.Keyword, @"^[ \t]{0,3}(?:#{1,6}(?=[ \t]|$).*$|(?:\x60{3,}|~{3,}).*$|>(?=[ \t])|(?:[-*+]|\d+[.)])(?=[ \t]))"),
        (Token.Type, @"!?\[[^\]\r\n]+\]\([^\r\n]*?\)"),
        (Token.String, Backtick + @"|\*\*[^*\r\n]+\*\*|__[^_\r\n]+__")));

    private static readonly Lazy<Highlighter> Shell = new(() => Build(
        (Token.Comment, @"(?:^|[ \t])#.*$"), (Token.String, Quoted),
        (Token.Keyword, Words("if|then|elif|else|fi|case|esac|for|in|do|done|while|until|select|function|return|export|local|readonly|unset|set|source|trap|exit|break|continue")),
        (Token.Type, ScriptVariable), (Token.Number, Number)));

    private static readonly Lazy<Highlighter> Dockerfile = new(() => Build(
        (Token.Comment, @"^[ \t]*#.*$"),
        (Token.Keyword, @"(?i:^[ \t]*(?:FROM|RUN|CMD|LABEL|MAINTAINER|EXPOSE|ENV|ADD|COPY|ENTRYPOINT|VOLUME|USER|WORKDIR|ARG|ONBUILD|STOPSIGNAL|HEALTHCHECK|SHELL)\b)"),
        (Token.String, Quoted), (Token.Type, ScriptVariable), (Token.Number, Number)));

    private static readonly Lazy<Highlighter> CMake = new(() => Build(
        (Token.Comment, "#.*$"), (Token.String, DoubleQuoted),
        (Token.Keyword, @"\b[A-Za-z_]\w*(?=[ \t]*\()"),
        (Token.Type, ScriptVariable + @"|\$<[^>]*>"),
        (Token.Number, Number + "|" + Words("ON|OFF|TRUE|FALSE|PUBLIC|PRIVATE|INTERFACE"))));

    private static readonly Lazy<Highlighter> TeX = new(() => Build(
        (Token.Comment, "%.*$"), (Token.Keyword, @"\\(?:[A-Za-z@]+|.)"),
        (Token.String, @"\${1,2}[^$\r\n]+\${1,2}"), (Token.Type, @"[{}]")));

    private const string ConfigSection = @"^[ \t]*\[\[?[^\]\r\n]+\]\]?";
    private const string ConfigKey = @"^[ \t]*[A-Za-z0-9_.-]+(?=[ \t]*=)";
    private static readonly Lazy<Highlighter> Ini = new(() => Build(
        (Token.Comment, @"^[ \t]*[;#!].*$"), (Token.Type, ConfigSection),
        (Token.Keyword, ConfigKey), (Token.String, Quoted),
        (Token.Number, Number + "|" + Words("true|false|yes|no|on|off"))));

    private static readonly Lazy<Highlighter> Toml = new(() => Build(
        (Token.Comment, "#.*$"), (Token.Type, ConfigSection),
        (Token.Keyword, @"^[ \t]*(?:" + Quoted + @"|[A-Za-z0-9_.-]+)(?=[ \t]*=)"),
        (Token.String, Quoted), (Token.Number, Number + "|" + Words("true|false|inf|nan"))));

    private static readonly Lazy<Highlighter> Yaml = new(() => Build(
        (Token.Comment, @"(?:^|[ \t])#.*$"),
        (Token.Keyword, @"(?:^[ \t]*(?:-[ \t]+)?)?(?:" + Quoted + @"|[A-Za-z_][\w .-]*)(?=:(?:[ \t]|$))"),
        (Token.Type, @"[&*!][A-Za-z_][\w.-]*|^[ \t]*(?:---|\.\.\.)(?=[ \t]|$)"),
        (Token.String, Quoted), (Token.Number, Number + "|" + Words("true|false|null"))));

    private const string LogClock = @"\d{2}:\d{2}:\d{2}(?:[.,]\d+)?(?:Z|[+-]\d{2}:?\d{2})?";
    private const string Ipv4Octet = @"(?:25[0-5]|2[0-4]\d|1\d\d|\d{1,2})";
    private static readonly Lazy<Highlighter> Log = new(() => Build(
        (Token.Comment, @"^[ \t]*(?:at[ \t]+\S.*|Traceback[ \t].*|File [\x22'][^\x22']+[\x22'], line \d+.*|--- End of [^\r\n]*stack trace[^\r\n]*|stack backtrace:.*|Caused by:.*)$"),
        (Token.Type, @"\b(?:\d{4}[-/]\d{2}[-/]\d{2}[Tt ]|[A-Za-z]{3}[ \t]+\d{1,2}[ \t]+)?" + LogClock + @"\b"),
        (Token.Keyword, @"(?i:" + Words("fatal|critical|crit|emerg|alert|panic|error|err|exception|severe") + ")"),
        (Token.String, @"(?i:" + Words("notice|info|information|success") + ")"),
        (Token.Number, @"(?i:" + Words("warn|warning") + @")|\b(?:" + Ipv4Octet + @"\.){3}" + Ipv4Octet + @"\b"),
        (Token.Number, @"(?<![0-9A-Fa-f:])(?:[0-9A-Fa-f]{0,4}:){2,7}[0-9A-Fa-f]{0,4}(?![0-9A-Fa-f:])"),
        (Token.Comment, @"(?i:" + Words("trace|debug|verbose") + ")")));
}
