using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace NotepadAvalonia.Services.Tools;

internal static class StructuredText
{
    internal static string FormatJson(string input, bool indent, string newline)
    {
        using var json = ParseJson(input);
        return WriteJson(json.RootElement.WriteTo, indent, newline);
    }

    internal static JsonDocument ParseJson(string input) =>
        JsonDocument.Parse(TextCodecs.Utf8.GetBytes(RemoveLeadingBom(input)));

    internal static string WriteJson(Action<Utf8JsonWriter> write, bool indent, string newline)
    {
        var bytes = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(bytes, new JsonWriterOptions
        {
            Indented = indent,
            NewLine = newline == "\r\n" ? "\r\n" : "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        }))
        {
            write(writer);
            writer.Flush();
        }
        string result = TextCodecs.Utf8.GetString(bytes.WrittenSpan);
        return newline == "\r" ? result.Replace('\n', '\r') : result;
    }

    internal static string FormatXml(string input, bool indent, string newline)
    {
        input = RemoveLeadingBom(input);
        // The bare XHTML5 doctype declares no entities. Keep it in the output and
        // validate the document with DTD processing disabled.
        string validationInput = RemoveBareHtmlDoctype(input);
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
        using (var check = XmlReader.Create(new StringReader(validationInput), settings))
            while (check.Read())
                if (check.Depth > 256) throw new FormatException("XML nesting exceeds 256 levels.");

        using var reader = XmlReader.Create(new StringReader(validationInput), settings);
        var document = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
        var root = document.Root!;
        if (root.Name.NamespaceName == "http://www.w3.org/1999/xhtml")
            return MarkupWhitespace.Format(input, html: false, indent, newline);
        if (!ReferenceEquals(input, validationInput)) throw new XmlException("DTDs are prohibited in XML formatting.");
        if (root.Attributes().Any(attribute => attribute.IsNamespaceDeclaration && IsXamlNamespace(attribute.Value)))
            return MarkupWhitespace.Format(input, html: false, indent, newline);
        FormatElement(root, indent, 0, preserve: false);
        foreach (var space in document.Nodes().OfType<XText>().Where(node => IsXmlWhitespace(node.Value)).ToArray())
            space.Remove();

        using var output = new StringWriter();
        using (var writer = XmlWriter.Create(output, new XmlWriterSettings
        {
            OmitXmlDeclaration = true,
            NewLineHandling = NewLineHandling.Entitize
        }))
            document.WriteTo(writer);
        string xml = indent ? output.ToString().Replace("\n", newline, StringComparison.Ordinal) : output.ToString();
        return document.Declaration is { } declaration ? declaration + (indent ? newline : "") + xml : xml;
    }

    private static string RemoveBareHtmlDoctype(string input)
    {
        int start = 0;
        while (true)
        {
            SkipXmlSpace(input, ref start);
            var tail = input.AsSpan(start);
            string? endMarker = tail.StartsWith("<?") ? "?>" : tail.StartsWith("<!--") ? "-->" : null;
            if (endMarker == null) break;
            int end = input.IndexOf(endMarker, start + 2, StringComparison.Ordinal);
            if (end < 0) return input;
            start = end + endMarker.Length;
        }

        if (!input.AsSpan(start).StartsWith("<!DOCTYPE")) return input;
        int name = start + 9, position = name;
        SkipXmlSpace(input, ref position);
        if (position == name || !input.AsSpan(position).StartsWith("html")) return input;
        position += 4;
        SkipXmlSpace(input, ref position);
        return position < input.Length && input[position] == '>'
            ? string.Concat(input.AsSpan(0, start), " ", input.AsSpan(position + 1))
            : input;
    }

    private static void SkipXmlSpace(string input, ref int position)
    {
        while (position < input.Length && input[position] is ' ' or '\t' or '\r' or '\n') position++;
    }

    private static bool IsXamlNamespace(string value) => value is "https://github.com/avaloniaui" or
        "http://schemas.microsoft.com/winfx/2006/xaml" or "http://schemas.microsoft.com/winfx/2006/xaml/presentation" ||
        value.StartsWith("using:", StringComparison.Ordinal) || value.StartsWith("clr-namespace:", StringComparison.Ordinal);

    private static void FormatElement(XElement element, bool indent, int depth, bool preserve)
    {
        preserve = (string?)element.Attribute(XNamespace.Xml + "space") switch
        {
            "preserve" => true,
            "default" => false,
            _ => preserve
        };
        var nodes = element.Nodes().ToArray();
        foreach (var child in nodes.OfType<XElement>()) FormatElement(child, indent, depth + 1, preserve);
        if (preserve || !nodes.Any(node => node is XElement) ||
            nodes.Any(node => node is XCData || node is XText text && !IsXmlWhitespace(text.Value)))
            return;

        foreach (var node in nodes)
        {
            if (node is XText) node.Remove();
            else if (indent) node.AddBeforeSelf(new XText("\n" + new string(' ', (depth + 1) * 2)));
        }
        if (indent) element.Add(new XText("\n" + new string(' ', depth * 2)));
    }

    private static bool IsXmlWhitespace(string value) => value.AsSpan().IndexOfAnyExcept(" \t\r\n") < 0;

    internal static string RemoveLeadingBom(string text) => text.StartsWith('\uFEFF') ? text[1..] : text;
}
