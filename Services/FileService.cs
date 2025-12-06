using System.IO;
using NotepadAvalonia.Models;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Runtime.InteropServices;

namespace NotepadAvalonia.Services;

/// <summary>
/// Maps to: function_14000ffe4 (load), function_14000f36c (save)
/// Encoding detection: function_140007f5c
/// </summary>
public class FileService 
{
    public async Task<(string content, Encoding encoding, FileEncodingType encodingType, LineEndingStyle lineEnding)> LoadFileAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        var encodingType = DetectEncodingType(bytes);
        var encoding = GetEncoding(encodingType);

        // Skip BOM if present
        int bomLength = GetBomLength(encoding, bytes);
        var content = encoding.GetString(bytes, bomLength, bytes.Length - bomLength);

        var lineEnding = DetectLineEndings(content);

        return (content, encoding, encodingType, lineEnding);
    }

    public async Task SaveFileAsync(string path, string content, FileEncodingType encodingType, LineEndingStyle lineEnding)
    {
        // Preserve existing line endings (do not normalize) to mirror classic Notepad behavior.
        _ = lineEnding;

        var encoding = GetEncoding(encodingType);

        // Get bytes with optional BOM
        byte[] contentBytes = encoding.GetBytes(content);
        byte[] bomBytes = encoding.GetPreamble();

        using var stream = File.Create(path);
        if (bomBytes.Length > 0)
        {
            await stream.WriteAsync(bomBytes);
        }
        await stream.WriteAsync(contentBytes);
    }

    /// <summary>
    /// Maps to function_140007f5c BOM detection logic
    /// </summary>
    public FileEncodingType DetectEncodingType(byte[] bytes)
    {
        if (bytes.Length < 2) return FileEncodingType.ANSI;

        // UTF-8 BOM: EF BB BF
        if (bytes.Length >= 3 &&
            bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return FileEncodingType.UTF8BOM;
        }

        // UTF-16 LE BOM: FF FE
        if (bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return FileEncodingType.UTF16LE;
        }

        // UTF-16 BE BOM: FE FF  
        if (bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return FileEncodingType.UTF16BE;
        }

        // Heuristic detection (like IsTextUnicode)
        var utf16Kind = LooksLikeUtf16(bytes);
        if (utf16Kind.HasValue) return utf16Kind.Value;

        if (LooksLikeUtf8(bytes)) return FileEncodingType.UTF8;

        return FileEncodingType.ANSI; // ANSI
    }

    public LineEndingStyle DetectLineEndings(string content)
    {
        int crlf = 0, lf = 0, cr = 0;

        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] == '\r')
            {
                if (i + 1 < content.Length && content[i + 1] == '\n')
                {
                    crlf++;
                    i++; // Skip the \n
                }
                else
                {
                    cr++;
                }
            }
            else if (content[i] == '\n')
            {
                lf++;
            }
        }

        if (crlf == 0 && lf == 0 && cr == 0)
            return LineEndingStyle.CRLF;

        if (crlf >= lf && crlf >= cr) return LineEndingStyle.CRLF;
        if (lf >= cr) return LineEndingStyle.LF;
        return LineEndingStyle.CR;
    }

    public Encoding GetEncoding(FileEncodingType encodingType)
    {
        return encodingType switch
        {
            FileEncodingType.ANSI => Encoding.Default,
            FileEncodingType.UTF16LE => Encoding.Unicode,              // with BOM
            FileEncodingType.UTF16BE => Encoding.BigEndianUnicode,     // with BOM
            FileEncodingType.UTF8BOM => new UTF8Encoding(true),
            FileEncodingType.UTF8 => new UTF8Encoding(false),
            _ => Encoding.Default
        };
    }

    private int GetBomLength(Encoding encoding, byte[] bytes)
    {
        if (bytes.Length >= 3 &&
            bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return 3;
        if (bytes.Length >= 2 &&
            ((bytes[0] == 0xFF && bytes[1] == 0xFE) ||
             (bytes[0] == 0xFE && bytes[1] == 0xFF)))
            return 2;
        return 0;
    }

    private bool LooksLikeUtf8(byte[] bytes)
    {
        int i = 0;
        while (i < bytes.Length)
        {
            if (bytes[i] <= 0x7F)
            {
                i++;
            }
            else if (bytes[i] >= 0xC2 && bytes[i] <= 0xDF)
            {
                if (i + 1 >= bytes.Length || (bytes[i + 1] & 0xC0) != 0x80)
                    return false;
                i += 2;
            }
            else if (bytes[i] >= 0xE0 && bytes[i] <= 0xEF)
            {
                if (i + 2 >= bytes.Length ||
                    (bytes[i + 1] & 0xC0) != 0x80 ||
                    (bytes[i + 2] & 0xC0) != 0x80)
                    return false;
                i += 3;
            }
            else
            {
                return false;
            }
        }
        return true;
    }

    private FileEncodingType? LooksLikeUtf16(byte[] bytes)
    {
        // Prefer Windows API when available for parity with core
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            int flags = 0;
            // 0x0001 IS_TEXT_UNICODE_ASCII16 | 0x0002 REVERSE_ASCII16 | 0x0004 STATISTICS | 0x0008 REVERSE_STATISTICS
            flags = 0x0001 | 0x0002 | 0x0004 | 0x0008;
            bool isUnicode = IsTextUnicode(bytes, bytes.Length, ref flags);
            if (isUnicode)
            {
                // Flags mirroring IS_TEXT_UNICODE_REVERSE_MASK vs UNICODE_MASK
                if ((flags & 0x0002) == 0x0002) return FileEncodingType.UTF16BE;
                return FileEncodingType.UTF16LE;
            }
        }

        // Fallback heuristic: look for 0x00 bytes in expected positions
        int evenZeros = 0, oddZeros = 0, count = Math.Min(bytes.Length, 4096);
        for (int i = 0; i + 1 < count; i += 2)
        {
            if (bytes[i] == 0) evenZeros++;
            if (bytes[i + 1] == 0) oddZeros++;
        }

        int pairs = count / 2;
        if (pairs == 0) return null;

        // If almost all even bytes are 0, likely BE; if almost all odd bytes are 0, likely LE
        if (oddZeros > pairs * 0.6) return FileEncodingType.UTF16LE;
        if (evenZeros > pairs * 0.6) return FileEncodingType.UTF16BE;

        return null;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool IsTextUnicode(byte[] buf, int len, ref int lpi);
}
