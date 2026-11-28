using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace NotepadAvalonia.Services.Tools;

internal enum Base64Format { Standard, Mime, Url }

internal static class TextCodecs
{
    internal static readonly Encoding Utf8 = new UTF8Encoding(false, true);

    internal static string EncodeBase64(string text, Base64Format format) => EncodeBase64(Utf8.GetBytes(text), format);

    internal static string EncodeBase64(byte[] bytes, Base64Format format)
    {
        string value = Convert.ToBase64String(bytes, format == Base64Format.Mime
            ? Base64FormattingOptions.InsertLineBreaks : Base64FormattingOptions.None);
        return format == Base64Format.Url ? value.TrimEnd('=').Replace('+', '-').Replace('/', '_') : value;
    }

    internal static string DecodeBase64(string text, Base64Format format) => Utf8.GetString(DecodeBase64Bytes(text, format));

    internal static byte[] DecodeBase64Bytes(string text, Base64Format format)
    {
        if (format == Base64Format.Mime)
            text = text.Replace(" ", "").Replace("\t", "").Replace("\r", "").Replace("\n", "");
        else if (text.AsSpan().IndexOfAny(" \t\r\n") >= 0)
            throw new FormatException("This Base64 format does not allow whitespace.");

        string input = text;
        if (format == Base64Format.Url)
        {
            if (text.AsSpan().IndexOfAny("+/=") >= 0 || text.Length % 4 == 1)
                throw new FormatException("Base64URL requires the URL-safe alphabet and no padding.");
            input = text.Replace('-', '+').Replace('_', '/') + new string('=', (4 - text.Length % 4) % 4);
        }

        byte[] bytes = Convert.FromBase64String(input);
        if (!string.Equals(EncodeBase64(bytes, format == Base64Format.Mime ? Base64Format.Standard : format),
                text, StringComparison.Ordinal))
            throw new FormatException("Invalid Base64 alphabet, padding, or unused bits.");
        return bytes;
    }

    internal static string EncodeUrl(string text, bool form)
    {
        _ = Utf8.GetByteCount(text);
        return form ? System.Net.WebUtility.UrlEncode(text) : Uri.EscapeDataString(text);
    }

    internal static string DecodeUrl(string text, bool form)
    {
        var output = new StringBuilder(text.Length);
        using var bytes = new MemoryStream();
        int index = 0;
        while (index < text.Length)
        {
            if (text[index] != '%')
            {
                char character = text[index++];
                output.Append(form && character == '+' ? ' ' : character);
                continue;
            }

            bytes.SetLength(0);
            while (index < text.Length && text[index] == '%')
            {
                if (index + 2 >= text.Length ||
                    !byte.TryParse(text.AsSpan(index + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte value))
                    throw new FormatException("Expected two hexadecimal digits after '%'.");
                bytes.WriteByte(value);
                index += 3;
            }
            output.Append(Utf8.GetString(bytes.GetBuffer(), 0, (int)bytes.Length));
        }
        return output.ToString();
    }

    internal static string EncodeQuotedPrintable(string text)
    {
        byte[] bytes = Utf8.GetBytes(text);
        var result = new StringBuilder();
        int width = 0;
        for (int i = 0; i < bytes.Length; i++)
        {
            byte value = bytes[i];
            if (value == 13 && i + 1 < bytes.Length && bytes[i + 1] == 10)
            {
                result.Append("\r\n");
                width = 0;
                i++;
                continue;
            }
            bool plain = value is >= 33 and <= 126 and not 61;
            int length = plain ? 1 : 3;
            if (width + length > 75)
            {
                result.Append("=\r\n");
                width = 0;
            }
            if (plain) result.Append((char)value);
            else result.Append('=').Append(value.ToString("X2", CultureInfo.InvariantCulture));
            width += length;
        }
        return result.ToString();
    }

    internal static string DecodeQuotedPrintable(string text)
    {
        using var output = new MemoryStream();
        for (int i = 0; i < text.Length; i++)
        {
            char value = text[i];
            if (value == '=')
            {
                if (text.AsSpan(i + 1).StartsWith("\r\n")) { i += 2; continue; }
                if (text.AsSpan(i + 1).StartsWith("\n")) { i++; continue; }
                if (i + 2 >= text.Length ||
                    !byte.TryParse(text.AsSpan(i + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out byte decoded))
                    throw new FormatException("Invalid quoted-printable escape.");
                output.WriteByte(decoded);
                i += 2;
            }
            else if (value is > '~' or < ' ' and not ('\t' or '\r' or '\n'))
                throw new FormatException("Quoted-printable input must use printable ASCII or byte escapes.");
            else
            {
                // RFC 2045 discards transport-added trailing whitespace before a hard line break.
                if (value is ' ' or '\t')
                {
                    int end = i + 1;
                    while (end < text.Length && text[end] is ' ' or '\t') end++;
                    if (end < text.Length && text[end] is not ('\r' or '\n'))
                        for (int j = i; j < end; j++) output.WriteByte((byte)text[j]);
                    i = end - 1;
                    continue;
                }
                output.WriteByte((byte)value);
            }
        }
        return Utf8.GetString(output.GetBuffer(), 0, (int)output.Length);
    }
}
