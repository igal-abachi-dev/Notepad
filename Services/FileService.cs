using System.IO;
using NotepadAvalonia.Models;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Runtime.InteropServices;
using System.Text.Unicode;

namespace NotepadAvalonia.Services;

/// <summary>
/// Maps to: function_14000ffe4 (load), function_14000f36c (save)
/// Encoding detection: function_140007f5c
/// </summary>
public class FileService 
{
    static FileService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<(string content, Encoding encoding, FileEncodingType encodingType, LineEndingStyle lineEnding)> LoadFileAsync(string path)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        var encodingType = DetectEncodingType(bytes);
        var encoding = GetEncoding(encodingType);

        // Skip BOM if present
        int bomLength = GetBomLength(bytes);
        var content = encoding.GetString(bytes, bomLength, bytes.Length - bomLength);

        var lineEnding = DetectLineEndings(content);

        return (content, encoding, encodingType, lineEnding);
    }

    public async Task SaveFileAsync(string path, string content, FileEncodingType encodingType, LineEndingStyle lineEnding)
    {
        // Preserve existing line endings (do not normalize) to mirror classic Notepad behavior.
        _ = lineEnding;

        var encoding = GetEncoding(encodingType);

        // Encode before touching the destination so unrepresentable characters fail safely.
        byte[] contentBytes = encoding.GetBytes(content);
        byte[] bomBytes = encoding.GetPreamble();

        string destination = Path.GetFullPath(path);
        if (File.Exists(destination))
            destination = File.ResolveLinkTarget(destination, returnFinalTarget: true)?.FullName ?? destination;

        string tempPath = Path.Combine(Path.GetDirectoryName(destination)!, $".notepad-{Guid.NewGuid():N}.tmp");
        bool created = false;
        try
        {
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, bufferSize: 4096, FileOptions.Asynchronous))
            {
                created = true;
                await stream.WriteAsync(bomBytes);
                await stream.WriteAsync(contentBytes);
            }

            if (File.Exists(destination))
            {
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(tempPath, File.GetUnixFileMode(destination));
                File.Replace(tempPath, destination, null);
            }
            else
            {
                File.Move(tempPath, destination);
            }
            created = false;
        }
        finally
        {
            if (created) File.Delete(tempPath);
        }
    }

    /// <summary>
    /// Maps to function_140007f5c BOM detection logic
    /// </summary>
    public FileEncodingType DetectEncodingType(byte[] bytes)
    {
        // UTF-8 BOM: EF BB BF
        if (bytes.Length >= 3 &&
            bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return FileEncodingType.UTF8BOM;
        }

        // UTF-16 LE BOM: FF FE
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return FileEncodingType.UTF16LE;
        }

        // UTF-16 BE BOM: FE FF  
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return FileEncodingType.UTF16BE;
        }

        bool isUtf8 = Utf8.IsValid(bytes);
        if (isUtf8 && Array.IndexOf(bytes, (byte)0) < 0) return FileEncodingType.UTF8;
        if (bytes.Length >= 2 && bytes.Length % 2 == 0 && LooksLikeUtf16(bytes) is { } utf16Kind)
            return utf16Kind;

        return isUtf8 ? FileEncodingType.UTF8 : FileEncodingType.ANSI;
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
            FileEncodingType.ANSI => Encoding.GetEncoding(
                OperatingSystem.IsWindows() ? Encoding.GetEncoding(0).CodePage : 1252,
                EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
            FileEncodingType.UTF16LE => new UnicodeEncoding(false, true, true),
            FileEncodingType.UTF16BE => new UnicodeEncoding(true, true, true),
            FileEncodingType.UTF8BOM => new UTF8Encoding(true, true),
            _ => new UTF8Encoding(false, true)
        };
    }

    private int GetBomLength(byte[] bytes)
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

    private FileEncodingType? LooksLikeUtf16(byte[] bytes)
    {
        // Check byte alignment first so BE text with NULs is recognized on all platforms.
        int evenZeros = 0, oddZeros = 0, count = Math.Min(bytes.Length, 4096);
        for (int i = 0; i + 1 < count; i += 2)
        {
            if (bytes[i] == 0) evenZeros++;
            if (bytes[i + 1] == 0) oddZeros++;
        }

        int pairs = count / 2;
        if (pairs == 0) return null;

        // If almost all even bytes are 0, likely BE; if almost all odd bytes are 0, likely LE
        if (oddZeros > evenZeros && oddZeros > pairs * 0.6) return FileEncodingType.UTF16LE;
        if (evenZeros > oddZeros && evenZeros > pairs * 0.6) return FileEncodingType.UTF16BE;

        if (OperatingSystem.IsWindows())
        {
            const int littleEndianTests = 0x0001 | 0x0002;
            const int bigEndianTests = 0x0010 | 0x0020;
            int flags = littleEndianTests | bigEndianTests;
            IsTextUnicode(bytes, bytes.Length, ref flags);
            if ((flags & littleEndianTests) != 0 && (flags & bigEndianTests) == 0)
                return FileEncodingType.UTF16LE;
            if ((flags & bigEndianTests) != 0 && (flags & littleEndianTests) == 0)
                return FileEncodingType.UTF16BE;
        }

        return null;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool IsTextUnicode(byte[] buf, int len, ref int lpi);
}
