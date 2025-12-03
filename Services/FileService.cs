using System.IO;
using NotepadAvalonia.Models;
using System.Text;
using System.Threading.Tasks;

namespace NotepadAvalonia.Services;

/// <summary>
/// Maps to: function_14000ffe4 (load), function_14000f36c (save)
/// Encoding detection: function_140007f5c
/// </summary>
public class FileService 
{
    public async Task<(string content, Encoding encoding, LineEndingStyle lineEnding)> LoadFileAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        var encoding = DetectEncoding(bytes);

        // Skip BOM if present
        int bomLength = GetBomLength(encoding, bytes);
        var content = encoding.GetString(bytes, bomLength, bytes.Length - bomLength);

        var lineEnding = DetectLineEndings(content);

        return (content, encoding, lineEnding);
    }

    public async Task SaveFileAsync(string path, string content, Encoding encoding, LineEndingStyle lineEnding)
    {
        // Normalize line endings
        content = NormalizeLineEndings(content, lineEnding);

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
    public Encoding DetectEncoding(byte[] bytes)
    {
        if (bytes.Length < 2) return Encoding.Default;

        // UTF-8 BOM: EF BB BF
        if (bytes.Length >= 3 &&
            bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return new UTF8Encoding(true);
        }

        // UTF-16 LE BOM: FF FE
        if (bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return Encoding.Unicode;
        }

        // UTF-16 BE BOM: FE FF  
        if (bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return Encoding.BigEndianUnicode;
        }

        // Heuristic detection (like IsTextUnicode)
        if (LooksLikeUtf8(bytes))
        {
            return new UTF8Encoding(false);
        }

        return Encoding.Default; // ANSI
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

        if (crlf >= lf && crlf >= cr) return LineEndingStyle.CRLF;
        if (lf >= cr) return LineEndingStyle.LF;
        return LineEndingStyle.CR;
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

    private string NormalizeLineEndings(string content, LineEndingStyle style)
    {
        // First normalize to \n
        content = content.Replace("\r\n", "\n").Replace("\r", "\n");

        return style switch
        {
            LineEndingStyle.CRLF => content.Replace("\n", "\r\n"),
            LineEndingStyle.CR => content.Replace("\n", "\r"),
            _ => content // LF
        };
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
}