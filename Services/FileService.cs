using System.IO;
using Notepad.NeoEdit;
using NotepadAvalonia.Models;
using System.Text;
using System.Threading.Tasks;
using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using System.Text.Unicode;

namespace NotepadAvalonia.Services;

/// <summary>
/// Maps to: function_14000ffe4 (load), function_14000f36c (save)
/// Encoding detection: function_140007f5c
/// </summary>
public partial class FileService
{
    private const int MaximumInputSize = 1024 * 1024 * 1024;

    static FileService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<(string content, Encoding encoding, FileEncodingType encodingType, LineEndingStyle lineEnding)> LoadFileAsync(
        string path, FileEncodingType? requestedEncoding = null, bool windowsOnly = false)
    {
        var file = await ReadDocumentAsync(path, requestedEncoding, windowsOnly);
        return (file.Content, file.Encoding, file.EncodingType, file.LineEnding);
    }

    public virtual Task<LoadedFile> ReadDocumentAsync(string path, FileEncodingType? requestedEncoding = null, bool windowsOnly = false) =>
        ReadDocumentCoreAsync(path, requestedEncoding, windowsOnly, replaceInvalidBytes: false);

    internal Task<LoadedFile> ReadDocumentWithReplacementAsync(string path, FileEncodingType? requestedEncoding, bool windowsOnly) =>
        ReadDocumentCoreAsync(path, requestedEncoding, windowsOnly, replaceInvalidBytes: true);

    private async Task<LoadedFile> ReadDocumentCoreAsync(string path, FileEncodingType? requestedEncoding,
        bool windowsOnly, bool replaceInvalidBytes)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        long length = stream.Length;
        if (length >= MaximumInputSize)
            throw new IOException("Notepad can open files smaller than 1 GiB.");

        var bytes = new byte[(int)length];
        await stream.ReadExactlyAsync(bytes);
        var encodingType = requestedEncoding ?? DetectEncodingType(bytes);
        var encoding = GetEncoding(encodingType);

        int bomLength = GetBomLength(bytes, encodingType);
        Encoding decoder = encoding;
        if (replaceInvalidBytes)
        {
            decoder = (Encoding)encoding.Clone();
            decoder.DecoderFallback = new DecoderReplacementFallback("\uFFFD");
        }
        var content = decoder.GetString(bytes, bomLength, bytes.Length - bomLength);

        if (windowsOnly) content = TextLineEndings.Normalize(content, "\r\n");
        var lineEnding = DetectLineEndings(content);

        return new(content, encoding, encodingType, lineEnding, new(bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes))));
    }

    public async Task SaveFileAsync(string path, string content, FileEncodingType encodingType, LineEndingStyle lineEnding) =>
        await CommitFileAsync(path, content, encodingType, lineEnding);

    public virtual Task<FileVersion> CommitFileAsync(string path, string content, FileEncodingType encodingType,
        LineEndingStyle lineEnding, FileVersion? expected = null) =>
        StageAndCommitFileAsync(path, content, encodingType, expected, hardLink: null);

    internal virtual Task<FileVersion> CommitNewFileAsync(string path, string content, FileEncodingType encodingType) =>
        StageAndCommitFileAsync(path, content, encodingType, expected: null, hardLink: null, createNew: true);

    internal Task<FileVersion> CommitHardLinkedFileAsync(string path, string content, FileEncodingType encodingType,
        FileVersion? expected, HardLinkSaveTarget hardLink) =>
        StageAndCommitFileAsync(path, content, encodingType, expected, hardLink);

    private async Task<FileVersion> StageAndCommitFileAsync(string path, string content, FileEncodingType encodingType,
        FileVersion? expected, HardLinkSaveTarget? hardLink, bool createNew = false)
    {
        // Preserve the separators already present in the editor buffer.
        var encoding = GetEncoding(encodingType);

        string destination = Path.GetFullPath(path);
        if (!createNew && File.Exists(destination))
            destination = File.ResolveLinkTarget(destination, returnFinalTarget: true)?.FullName ?? destination;

        string tempPath = Path.Combine(Path.GetDirectoryName(destination)!, $".notepad-{Guid.NewGuid():N}.tmp");
        bool created = false;
        try
        {
            FileVersion written;
            await using (var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None, bufferSize: 4096, FileOptions.Asynchronous))
            {
                created = true;
                // Stream conversion into the staging file. Strict encoding failures leave the destination intact.
                await using (var writer = new StreamWriter(stream, encoding, bufferSize: 8192, leaveOpen: true))
                    await writer.WriteAsync(content.AsMemory());
                await Task.Run(() => stream.Flush(flushToDisk: true));
                stream.Position = 0;
                written = await ReadVersionAsync(stream);
            }

            await Task.Run(() =>
            {
                if (createNew)
                    File.Move(tempPath, destination);
                else if (hardLink is { } approved)
                    CommitHardLinkedFile(tempPath, destination, expected, approved);
                else
                    CommitStagedFile(tempPath, destination, expected);
            });
            return written;
        }
        finally
        {
            if (created) DeleteSaveArtifact(tempPath);
        }
    }

    private static async Task<FileVersion> ReadVersionAsync(FileStream stream) =>
        new(stream.Length, Convert.ToHexString(await SHA256.HashDataAsync(stream)));

    private static void CommitStagedFile(string staging, string destination, FileVersion? expected)
    {
        string pathKey = OperatingSystem.IsWindows() ? destination.ToUpperInvariant() : destination;
        string digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(pathKey)));
        using var mutex = new Mutex(false, "NotepadAvalonia-save-" + digest);
        bool acquired;
        try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new IOException("Another Notepad process is still saving this file.");

        try
        {
            using var original = expected == null ? null : OpenForVersionCheck(destination);
            if (original != null) VerifyVersion(original, expected!);
            bool exists = OperatingSystem.IsWindows()
                ? CheckHardLinks(destination, original, expected)
                : original != null || File.Exists(destination);

            if (exists)
            {
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(staging, File.GetUnixFileMode(destination));
                File.Replace(staging, destination, null);
            }
            else
                File.Move(staging, destination);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static FileVersion ReadVersion(FileStream stream)
    {
        stream.Position = 0;
        return new(stream.Length, Convert.ToHexString(SHA256.HashData(stream)));
    }

    private static void VerifyVersion(FileStream stream, FileVersion expected)
    {
        if (stream.Length != expected.Length || ReadVersion(stream) != expected)
            throw new FileChangedException();
    }

    private static void DeleteSaveArtifact(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Could not remove a Notepad save artifact: {0}", ex.GetType().Name);
        }
    }

    private static FileStream OpenForVersionCheck(string path)
    {
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                bufferSize: 8192, FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new FileChangedException();
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

    private static int GetBomLength(byte[] bytes, FileEncodingType kind)
    {
        return kind switch
        {
            FileEncodingType.UTF8 or FileEncodingType.UTF8BOM when bytes.AsSpan().StartsWith("\uFEFF"u8) => 3,
            FileEncodingType.UTF16LE when bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE => 2,
            FileEncodingType.UTF16BE when bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF => 2,
            _ => 0
        };
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
        if (pairs < 4) return null; // Very short NUL-containing UTF-8 can look like UTF-16.

        // If almost all even bytes are 0, likely BE; if almost all odd bytes are 0, likely LE
        FileEncodingType? candidate = oddZeros > evenZeros && oddZeros > pairs * 0.6 ? FileEncodingType.UTF16LE
            : evenZeros > oddZeros && evenZeros > pairs * 0.6 ? FileEncodingType.UTF16BE : null;
        if (candidate == null) return null;
        try
        {
            _ = GetEncoding(candidate.Value).GetCharCount(bytes);
            return candidate;
        }
        catch (DecoderFallbackException) { return null; }
    }
}

public sealed record LoadedFile(string Content, Encoding Encoding, FileEncodingType EncodingType,
    LineEndingStyle LineEnding, FileVersion Version);

public sealed class FileChangedException() : IOException("The file changed or was removed on disk. Use Save As to save your edits to another file.");
