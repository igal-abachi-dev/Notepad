using System;
using System.Globalization;

namespace NotepadAvalonia.Services.Tools;

internal enum TextTool
{
    Base64Encode, Base64Decode, Base64MimeEncode, Base64MimeDecode, Base64UrlEncode, Base64UrlDecode,
    QuotedPrintableEncode, QuotedPrintableDecode, UrlEncode, UrlDecode, FormEncode, FormDecode,
    BasicEncode, BasicDecode, JwtDecode, JwtEncode, JweInspect, JweAssemble, JwkBeautify, JwkMinify,
    JsonBeautify, JsonMinify, XmlBeautify, XmlMinify, HtmlBeautify, HtmlMinify,
    CssBeautify, CssMinify, JsBeautify, JsMinify, TsBeautify, TsMinify,
    UpperCase, LowerCase, TitleCase, CamelCase
}

internal static class TextTools
{
    internal static string Apply(TextTool tool, string text, string newline) => tool switch
    {
        TextTool.Base64Encode => TextCodecs.EncodeBase64(text, Base64Format.Standard),
        TextTool.Base64Decode => TextCodecs.DecodeBase64(text, Base64Format.Standard),
        TextTool.Base64MimeEncode => TextCodecs.EncodeBase64(text, Base64Format.Mime),
        TextTool.Base64MimeDecode => TextCodecs.DecodeBase64(text, Base64Format.Mime),
        TextTool.Base64UrlEncode => TextCodecs.EncodeBase64(text, Base64Format.Url),
        TextTool.Base64UrlDecode => TextCodecs.DecodeBase64(text, Base64Format.Url),
        TextTool.QuotedPrintableEncode => TextCodecs.EncodeQuotedPrintable(text),
        TextTool.QuotedPrintableDecode => TextCodecs.DecodeQuotedPrintable(text),
        TextTool.UrlEncode => TextCodecs.EncodeUrl(text, form: false),
        TextTool.UrlDecode => TextCodecs.DecodeUrl(text, form: false),
        TextTool.FormEncode => TextCodecs.EncodeUrl(text, form: true),
        TextTool.FormDecode => TextCodecs.DecodeUrl(text, form: true),
        TextTool.BasicEncode => "Basic " + TextCodecs.EncodeBase64(ValidateBasic(text), Base64Format.Standard),
        TextTool.BasicDecode => DecodeBasic(text),
        TextTool.JwtDecode => JoseText.DecodeJwt(text, newline),
        TextTool.JwtEncode => JoseText.EncodeUnsignedJwt(text),
        TextTool.JweInspect => JoseText.InspectJwe(text, newline),
        TextTool.JweAssemble => JoseText.AssembleJwe(text),
        TextTool.JwkBeautify => JoseText.FormatJwk(text, true, newline),
        TextTool.JwkMinify => JoseText.FormatJwk(text, false, newline),
        TextTool.JsonBeautify => StructuredText.FormatJson(text, true, newline),
        TextTool.JsonMinify => StructuredText.FormatJson(text, false, newline),
        TextTool.XmlBeautify => StructuredText.FormatXml(text, true, newline),
        TextTool.XmlMinify => StructuredText.FormatXml(text, false, newline),
        TextTool.HtmlBeautify => MarkupWhitespace.Format(text, html: true, indent: true, newline),
        TextTool.HtmlMinify => MarkupWhitespace.Format(text, html: true, indent: false, newline),
        TextTool.CssBeautify => SourceWhitespace.Format(text, css: true, indent: true, newline),
        TextTool.CssMinify => SourceWhitespace.Format(text, css: true, indent: false, newline),
        TextTool.JsBeautify => SourceWhitespace.Format(text, css: false, indent: true, newline),
        TextTool.JsMinify => SourceWhitespace.Format(text, css: false, indent: false, newline),
        TextTool.TsBeautify => SourceWhitespace.Format(text, css: false, indent: true, newline, typeScript: true),
        TextTool.TsMinify => SourceWhitespace.Format(text, css: false, indent: false, newline, typeScript: true),
        TextTool.UpperCase => text.ToUpper(CultureInfo.CurrentCulture),
        TextTool.LowerCase => text.ToLower(CultureInfo.CurrentCulture),
        TextTool.TitleCase => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(text.ToLower(CultureInfo.CurrentCulture)),
        TextTool.CamelCase => TextCasing.ToCamelCase(text, CultureInfo.CurrentCulture.TextInfo),
        _ => throw new ArgumentOutOfRangeException(nameof(tool))
    };

    private static string DecodeBasic(string text)
    {
        text = text.Trim();
        if (text.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase)) text = text[14..].TrimStart();
        if (!text.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Expected a Basic authorization value.");
        return ValidateBasic(TextCodecs.DecodeBase64(text[6..].TrimStart(), Base64Format.Standard));
    }

    private static string ValidateBasic(string text)
    {
        if (!text.Contains(':')) throw new FormatException("Expected Basic credentials in username:password form.");
        foreach (char character in text)
        {
            if (character < ' ' || character == '\u007F')
                throw new FormatException("Basic credentials cannot contain ASCII control characters.");
        }
        return text;
    }
}
