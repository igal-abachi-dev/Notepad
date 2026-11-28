using System;
using System.Linq;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Notepad.NeoEdit;
using NotepadAvalonia.Services.Tools;
using Xunit;

namespace Notepad.Tests;

public class TextToolsTests
{
    [Fact]
    public void ChangeCase_HandlesWordsAndAcronymBoundaries()
    {
        Assert.Equal("ALPHA BETA", Apply(TextTool.UpperCase, "Alpha Beta"));
        Assert.Equal("alpha beta", Apply(TextTool.LowerCase, "Alpha Beta"));
        Assert.Equal("Alpha Beta", Apply(TextTool.TitleCase, "ALPHA beta"));
        Assert.Equal("httpServerVersion2Value", Apply(TextTool.CamelCase, "HTTPServer version2_value"));
    }

    [Theory]
    [InlineData("Base64Encode", "Base64Decode")]
    [InlineData("Base64MimeEncode", "Base64MimeDecode")]
    [InlineData("Base64UrlEncode", "Base64UrlDecode")]
    [InlineData("QuotedPrintableEncode", "QuotedPrintableDecode")]
    [InlineData("UrlEncode", "UrlDecode")]
    [InlineData("FormEncode", "FormDecode")]
    public void Codecs_RoundTripUnicodeControlsAndMixedEndings(string encodeName, string decodeName)
    {
        var encode = Enum.Parse<TextTool>(encodeName);
        var decode = Enum.Parse<TextTool>(decodeName);
        string original = new string('x', 104) + " café +\u0000=\r\n\t\rnext\nend\U0001F600";
        string encoded = Apply(encode, original);
        Assert.Equal(original, Apply(decode, encoded));
        if (encode is TextTool.QuotedPrintableEncode or TextTool.Base64MimeEncode)
            Assert.All(encoded.Split("\r\n"), row => Assert.InRange(row.Length, 0, 76));
    }

    [Fact]
    public void Codecs_DistinguishProtocolsAndRejectInvalidData()
    {
        Assert.Throws<FormatException>(() => Apply(TextTool.Base64Decode, " Zg==\r\n"));
        Assert.Equal("f", Apply(TextTool.Base64MimeDecode, " Zg==\r\n"));
        Assert.Equal("f", Apply(TextTool.Base64UrlDecode, "Zg"));
        Assert.Throws<FormatException>(() => Apply(TextTool.Base64UrlDecode, "Zg=="));
        Assert.Throws<FormatException>(() => Apply(TextTool.Base64Decode, "Zh=="));
        Assert.Throws<System.Text.DecoderFallbackException>(() => Apply(TextTool.Base64Decode, "/w=="));
        Assert.Throws<FormatException>(() => Apply(TextTool.UrlDecode, "bad%2x"));
        Assert.Equal("a+b c", Apply(TextTool.UrlDecode, "a+b%20c"));
        Assert.Equal("a b+c", Apply(TextTool.FormDecode, "a+b%2Bc"));
        Assert.Equal("ab\r\n", Apply(TextTool.QuotedPrintableDecode, "a=\r\nb \t\r\n"));
        Assert.Equal("u:p", Apply(TextTool.BasicDecode, "Authorization: " + Apply(TextTool.BasicEncode, "u:p")));
        Assert.Throws<FormatException>(() => Apply(TextTool.BasicEncode, "u:p\n"));
        Assert.Throws<FormatException>(() => Apply(TextTool.BasicDecode, "Basic dXNlcg=="));
    }

    [Fact]
    public void Jose_InspectsUnsignedJwtAndReassemblesOriginalProtectedJwe()
    {
        string jwt = Apply(TextTool.JwtEncode, """{"sub":"a","active":true}""");
        using var decoded = JsonDocument.Parse(Apply(TextTool.JwtDecode, jwt));
        Assert.Equal("none", decoded.RootElement.GetProperty("header").GetProperty("alg").GetString());
        Assert.True(decoded.RootElement.GetProperty("payload").GetProperty("active").GetBoolean());
        Assert.EndsWith(".", jwt);
        Assert.Throws<FormatException>(() => Apply(TextTool.JwtDecode, jwt + "YQ"));
        const string header = """{"alg":"dir", "enc":"A256GCM"}""";
        string jwe = Apply(TextTool.Base64UrlEncode, header) + "..YQ.Yg.Yw";
        string expanded = Apply(TextTool.JweInspect, jwe);
        Assert.Equal(jwe, Apply(TextTool.JweAssemble, expanded));
        Assert.Throws<FormatException>(() => Apply(TextTool.JweAssemble, expanded.Replace("A256GCM", "changed")));
        Assert.Equal("""{"kty":"oct","k":"YQ"}""", Apply(TextTool.JwkMinify, """{ "kty":"oct", "k":"YQ" }"""));
    }

    [Fact]
    public void StructuredFormats_KeepJsonValuesAndPreserveXmlMixedContent()
    {
        string json = Apply(TextTool.JsonBeautify, """{"n":100000000000000000000,"s":" a \n b ","d":1,"d":2}""");
        Assert.Contains("\r\n", json);
        Assert.Equal("""{"n":100000000000000000000,"s":" a \n b ","d":1,"d":2}""", Apply(TextTool.JsonMinify, json));
        Assert.ThrowsAny<JsonException>(() => Apply(TextTool.JsonMinify, "{invalid}"));
        const string xml = """<?xml version="1.0" encoding="utf-8"?><root><plain xml:space="preserve"> <a/> </plain><mixed>One <b/> two&#13;</mixed><elements> <a/> <b/> </elements></root>""";
        string pretty = Apply(TextTool.XmlBeautify, xml);
        string compact = Apply(TextTool.XmlMinify, pretty);
        Assert.StartsWith("""<?xml version="1.0" encoding="utf-8"?>""", compact);
        var parsed = XDocument.Parse(compact, LoadOptions.PreserveWhitespace);
        Assert.Equal("  ", parsed.Root!.Element("plain")!.Value);
        Assert.Equal("One  two\r", parsed.Root.Element("mixed")!.Value);
        Assert.Equal(2, parsed.Root.Element("elements")!.Nodes().Count());
        Assert.Throws<XmlException>(() => Apply(TextTool.XmlBeautify, "<!DOCTYPE p SYSTEM 'offline.dtd'><p/>"));
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    [InlineData("\r")]
    public void JsonAndJwtFormatting_UseExactlyOneDocumentLineEnding(string newline)
    {
        const string input = """{"message":"first\nsecond"}""";
        string json = TextTools.Apply(TextTool.JsonBeautify, input, newline);
        Assert.Equal(string.Join(newline, new[] { "{", "  \"message\": \"first\\nsecond\"", "}" }), json);

        string jwt = TextTools.Apply(TextTool.JwtEncode, input, newline);
        var lines = TextTools.Apply(TextTool.JwtDecode, jwt, newline).Split(newline);
        Assert.All(lines, line =>
        {
            Assert.NotEmpty(line);
            Assert.DoesNotContain('\r', line);
            Assert.DoesNotContain('\n', line);
        });
        Assert.Contains("    \"message\": \"first\\nsecond\"", lines);
    }

    [Fact]
    public void SourceFormatting_ProtectsJavaScriptNewlinesLiteralsAndCssDescendants()
    {
        const string js = "function f(){return\n{value: /[a/b]+/.test(\"a b\")};}// end";
        string min = Apply(TextTool.JsMinify, js);
        string pretty = Apply(TextTool.JsBeautify, js);
        Assert.Contains("return\n{", min);
        Assert.Contains("return\n", pretty);
        Assert.Contains("/[a/b]+/.test(\"a b\")", pretty);
        Assert.EndsWith("// end", pretty);
        Assert.Throws<FormatException>(() => Apply(TextTool.JsMinify, "let x = <div> hello </div>;"));
        const string template = "\u0060some \u0024{call(\u0060nested \u0024{3}\u0060)}\u0060";
        Assert.Contains(template, Apply(TextTool.JsBeautify, "let t = " + template + ";"));
        Assert.Throws<FormatException>(() => Apply(TextTool.JsMinify, "if (x) /a/.test(y);"));
        Assert.Throws<FormatException>(() => Apply(TextTool.JsMinify, "call([a);"));
        Assert.Contains("<!-- { comment", Apply(TextTool.JsBeautify, "<!-- { comment\nx();"));
        Assert.Contains("/>/g", Apply(TextTool.JsMinify, "const close = />/g;"));
        const string css = ".a   .b:not([aria-label='a  b'])   { content: \"a   b\"; color: red; }";
        string cssMin = Apply(TextTool.CssMinify, css);
        Assert.Equal(".a .b:not([aria-label='a  b']){content: \"a   b\";color: red;}", cssMin);
        Assert.Contains("\r\n  color: red;", Apply(TextTool.CssBeautify, css));
        const string custom = "--value:  { a:  b; }  ";
        Assert.Contains(custom, Apply(TextTool.CssMinify, "a { " + custom + "; color: red; }"));
        Assert.Throws<FormatException>(() => Apply(TextTool.CssMinify, "a { color: red;"));
    }

    [Theory]
    [InlineData("TsBeautify")]
    [InlineData("TsMinify")]
    public void TypeScriptFormatting_PreservesAssertionsGenericsAndTypeLiterals(string command)
    {
        var tool = Enum.Parse<TextTool>(command);
        const string input = "type Box<T> = { value: T; };\nconst id = <T>(value: T): T => value;\nconst n = <number>id(1);\nconst s = `value: ${<number>n}`;";
        string output = Apply(tool, input);
        Assert.Contains("Box<T>", output);
        Assert.Contains("<T>(value: T): T => value", output);
        Assert.Contains("<number>id(1)", output);
        Assert.Contains("`value: ${<number>n}`", output);
        Assert.Equal(output, Apply(tool, output));
        Assert.Throws<FormatException>(() => Apply(tool, "const view = <Widget />;"));
    }

    [Theory]
    [InlineData("CssBeautify")]
    [InlineData("CssMinify")]
    public void CssFormatting_PreservesTailwindDirectivesModulesAndUnquotedUrls(string command)
    {
        const string input = "@theme { --my-size: calc(100% - 2px); } .card :global(.widget) { composes: base from './base.module.css'; @apply flex hover:bg-blue-500; background: image-set(url(https://example.test/a/*) 1x); background-image: URL(data:image/svg+xml,<svg/>); @media (width >= 30rem) { color: oklch(65% .15 220); } }";
        var tool = Enum.Parse<TextTool>(command);
        string output = Apply(tool, input);
        Assert.Contains("--my-size: calc(100% - 2px)", output);
        Assert.Contains(".card :global(.widget)", output);
        Assert.Contains("composes: base from './base.module.css'", output);
        Assert.Contains("@apply flex hover:bg-blue-500", output);
        Assert.Contains("image-set(url(https://example.test/a/*) 1x)", output);
        Assert.Contains("URL(data:image/svg+xml,<svg/>)", output);
        Assert.Contains("@media (width >= 30rem)", output);
        Assert.Equal(output, Apply(tool, output));
    }

    [Fact]
    public void HtmlFormatting_PreservesTextOptionalTagsEntitiesAndRawContent()
    {
        const string input = "<!doctype html><title>A &nbsp; B</title><ul><li>one<li>two</ul><P><b>a</b> <i>b</i><BR><input  value = a/  disabled><img  src = x /></P><textarea>\n <fake   attr=value> &nbsp;</textarea><script>let x = '<tag   a = b>'; </script><pre> a\n b </pre>";
        string compact = Apply(TextTool.HtmlMinify, input);
        Assert.Equal(input.Replace("<input  value = a/  disabled><img  src = x />", "<input value=a/ disabled><img src=x />"), compact);
        string pretty = Apply(TextTool.HtmlBeautify, compact);
        Assert.Contains("<input\r\n  value=a/\r\n  disabled\r\n>", pretty);
        Assert.Equal(compact, Apply(TextTool.HtmlMinify, pretty));
        Assert.Equal(pretty, Apply(TextTool.HtmlBeautify, pretty));
        Assert.Equal("<img src=x/>", Apply(TextTool.HtmlMinify, "<img  src = x/>"));
        Assert.Equal("<br/><img src='x'/>", Apply(TextTool.HtmlMinify, "<br /><img src='x' />"));
    }

    [Fact]
    public void XhtmlFormatting_PreservesInlineWhitespaceAndAllowsOnlyTheBareHtmlDoctype()
    {
        const string xhtml = """<?xml version="1.0"?><!-- comment --><!DOCTYPE html><html xmlns = "http://www.w3.org/1999/xhtml"><body><p><span>a</span> <span>b</span><br /></p><p><span>a</span><span>b</span><![CDATA[<tag  a = 'b'>]]></p></body></html>""";
        foreach (var tool in new[] { TextTool.XmlBeautify, TextTool.XmlMinify })
        {
            string output = Apply(tool, xhtml);
            Assert.Contains("<!DOCTYPE html>", output);
            Assert.Contains("<span>a</span> <span>b</span>", output);
            Assert.Contains("<span>a</span><span>b</span><![CDATA[<tag  a = 'b'>]]>", output);
            Assert.Contains("xmlns=\"http://www.w3.org/1999/xhtml\"", output);
        }
        Assert.Throws<XmlException>(() => Apply(TextTool.XmlMinify, xhtml.Replace("<!DOCTYPE html>", "<!DOCTYPE html SYSTEM 'remote.dtd'>")));
        Assert.Throws<XmlException>(() => Apply(TextTool.XmlMinify, "<!DOCTYPE html>" + xhtml.Replace("<!DOCTYPE html>", "")));
    }

    [Theory]
    [InlineData("https://github.com/avaloniaui")]
    [InlineData("http://schemas.microsoft.com/winfx/2006/xaml/presentation")]
    public void XamlFormatting_PreservesWhitespaceBetweenRunsAndBindingValues(string namespaceName)
    {
        string xaml = $"<TextBlock xmlns = '{namespaceName}' Tag = '{{Binding  Name}}'><Run Text='a'/> <Run Text='b'/></TextBlock>";
        foreach (var tool in new[] { TextTool.XmlBeautify, TextTool.XmlMinify })
        {
            string output = Apply(tool, xaml);
            var root = XDocument.Parse(output, LoadOptions.PreserveWhitespace).Root!;
            Assert.Equal(" ", root.Value);
            Assert.Equal("{Binding  Name}", (string?)root.Attribute("Tag"));
            Assert.Equal(namespaceName, root.Name.NamespaceName);
        }
    }

    [Fact]
    public void NonPrintingMarkers_ExposeWhitespaceAndSupplementaryFormatCharacters()
    {
        Assert.Equal("·", NeoEditor.NonPrintingName(new(' ')));
        Assert.Equal("→", NeoEditor.NonPrintingName(new('\t')));
        Assert.Equal("NUL", NeoEditor.NonPrintingName(new(0)));
        Assert.Equal("U+007F", NeoEditor.NonPrintingName(new(0x7f)));
        Assert.Equal("U+E0100", NeoEditor.NonPrintingName(new(0xe0100)));
        Assert.Null(NeoEditor.NonPrintingName(new(0x1f600)));
    }

    [Fact]
    public void CharacterInspector_ReportsScalarValuesAndHtmlEntitiesWithoutTruncatingAtNul()
    {
        string description = CharacterInspector.Describe("\0\u200B\u00A0\U0001F600", selection: true, truncated: false);
        Assert.Contains("U+0000  NUL", description);
        Assert.DoesNotContain("&#x0;", description);
        Assert.Contains("U+200B  Zero-width space  ·  &ZeroWidthSpace;", description);
        Assert.Contains("U+00A0  Non-breaking space  ·  &nbsp;", description);
        Assert.Contains("U+1F600", description);
        Assert.Equal("No character at the caret.", CharacterInspector.Describe("", selection: false, truncated: false));
    }

    [Fact]
    public void CopySelection_ExtractsOnePowerShellFenceAndPreservesInteriorText()
    {
        const string code = "  Write-Output 'value'\r\n";
        Assert.Equal((code, ".ps1"), FencedSelection.Extract("```powershell\r\n" + code + "```\r\n"));
        Assert.Equal(SyntaxLanguage.PowerShell, SyntaxMatcher.DetectLanguage(".ps1"));
        const string mixed = "```powershell\nGet-Date\n```\nother text";
        Assert.Equal((mixed, null), FencedSelection.Extract(mixed));
        Assert.Equal(("text\0more", null), FencedSelection.Extract("text\0more"));
    }

    private static string Apply(TextTool tool, string value) => TextTools.Apply(tool, value, "\r\n");
}
