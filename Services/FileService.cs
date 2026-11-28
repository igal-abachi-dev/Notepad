using Microsoft.Win32.SafeHandles;
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
    internal const int LargeFileWarningSize = 200 * 1024 * 1024;
    private const UnixFileMode UnixWriteBits = UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite;

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

    public virtual async Task<LoadedFile> ReadDocumentAsync(string path, FileEncodingType? requestedEncoding = null,
        bool windowsOnly = false) =>
        (await ReadDocumentCoreAsync(path, requestedEncoding, windowsOnly, replaceInvalidBytes: false, confirmLargeFile: null))!;

    internal async Task<LoadedFile> ReadDocumentWithReplacementAsync(string path, FileEncodingType? requestedEncoding, bool windowsOnly) =>
        (await ReadDocumentCoreAsync(path, requestedEncoding, windowsOnly, replaceInvalidBytes: true, confirmLargeFile: null))!;

    internal virtual Task<LoadedFile?> ReadDocumentForEditorAsync(string path, FileEncodingType? requestedEncoding,
        bool windowsOnly, Func<long, Task<bool>> confirmLargeFile, bool replaceInvalidBytes = false) =>
        ReadDocumentCoreAsync(path, requestedEncoding, windowsOnly, replaceInvalidBytes, confirmLargeFile);

    private async Task<LoadedFile?> ReadDocumentCoreAsync(string path, FileEncodingType? requestedEncoding,
        bool windowsOnly, bool replaceInvalidBytes, Func<long, Task<bool>>? confirmLargeFile)
    {
        path = DocumentPath.GetFullPath(path);
        string openedPath = OperatingSystem.IsWindows() ? path : ResolveExistingTarget(path) ?? throw new FileNotFoundException(null, path);
        await using var stream = new FileStream(openedPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        string target = OperatingSystem.IsWindows() ? GetWindowsTarget(stream.SafeFileHandle) : openedPath;
        long length = stream.Length;
        if (RequiresLargeFileConfirmation(length) && confirmLargeFile != null && !await confirmLargeFile(length))
            return null;

        // Helix's stateful decoder and rope builder are a reference for a future large-file path.
        // A chunked loader must preserve whole-file encoding detection and the original-byte fingerprint.
        var bytes = new byte[(int)length];
        await stream.ReadExactlyAsync(bytes).ConfigureAwait(false);
        var encodingType = requestedEncoding ?? DetectEncodingType(bytes, preferOem437: DocumentPath.IsAnsiArt(path));
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

        bool readOnly = false;
        try
        {
            readOnly = (File.GetAttributes(stream.SafeFileHandle) & FileAttributes.ReadOnly) != 0 ||
                ((OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) &&
                    (File.GetUnixFileMode(stream.SafeFileHandle) & UnixWriteBits) == 0);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Metadata can be unavailable even when the file data is readable.
        }
        var version = new FileVersion(bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes))) { ResolvedPath = target };
        return new(content, encoding, encodingType, lineEnding, version)
        {
            IsReadOnly = readOnly
        };
    }

    internal static bool RequiresLargeFileConfirmation(long length)
    {
        if (length >= MaximumInputSize)
            throw new IOException("Notepad can open files smaller than 1 GiB.");
        return length > LargeFileWarningSize;
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

        path = DocumentPath.GetFullPath(path);
        string destination = ResolveSaveTarget(path, expected, createNew);
        if (!createNew) CheckDestinationReadOnly(destination);

        string tempPath = Path.Combine(Path.GetDirectoryName(destination)!, $".notepad-{Guid.NewGuid():N}.tmp");
        bool created = false;
        try
        {
            FileVersion written;
            var stagingOptions = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                Share = FileShare.None,
                Options = FileOptions.Asynchronous
            };
            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                stagingOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            await using (var stream = new FileStream(tempPath, stagingOptions))
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
                if (createNew || hardLink.HasValue)
                {
                    VerifySaveTarget(path, destination, expected, createNew);
                    if (hardLink is { } approved)
                    {
                        CheckDestinationReadOnly(destination);
                        CommitHardLinkedFile(tempPath, destination, expected, approved);
                    }
                    else File.Move(tempPath, destination);
                }
                else
                    CommitStagedFile(tempPath, path, destination, expected);
            });
            return written with { ResolvedPath = destination };
        }
        finally
        {
            if (created) DeleteSaveArtifact(tempPath);
        }
    }

    private static async Task<FileVersion> ReadVersionAsync(FileStream stream) =>
        new(stream.Length, Convert.ToHexString(await SHA256.HashDataAsync(stream)));

    private static void CommitStagedFile(string staging, string path, string destination, FileVersion? expected)
    {
        // Serialize saves through aliases of the same resolved path.
        string pathKey = OperatingSystem.IsMacOS() ? destination.Normalize(NormalizationForm.FormC).ToUpperInvariant() :
            OperatingSystem.IsWindows() ? destination.ToUpperInvariant() : destination;
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
            CheckDestinationReadOnly(destination);
            bool exists = OperatingSystem.IsWindows() || OperatingSystem.IsLinux()
                ? CheckHardLinks(destination, original, expected)
                : original != null || File.Exists(destination);

            VerifySaveTarget(path, destination, expected, createNew: false);
            if (exists)
            {
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                {
                    // Renaming a sibling file can succeed even without permission to write the destination.
                    using var writeCheck = OpenForWriteCheck(destination);
                    var mode = File.GetUnixFileMode(writeCheck);
                    if ((mode & UnixWriteBits) == 0 ||
                        (File.GetAttributes(writeCheck) & FileAttributes.ReadOnly) != 0)
                        throw new ReadOnlyFileException();
                    const UnixFileMode specialModes = UnixFileMode.SetUser | UnixFileMode.SetGroup | UnixFileMode.StickyBit;
                    File.SetUnixFileMode(staging, mode & ~specialModes);
                }
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

    internal static bool IsReadOnlyFile(string path)
    {
        var file = new FileInfo(path);
        if (!file.Exists) return false;
        return IsDestinationReadOnly(file.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path);
    }

    private static void CheckDestinationReadOnly(string path)
    {
        if (IsDestinationReadOnly(path)) throw new ReadOnlyFileException();
    }

    private static bool IsDestinationReadOnly(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0 ||
                ((OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) &&
                    (File.GetUnixFileMode(path) & UnixWriteBits) == 0);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static SafeFileHandle OpenForWriteCheck(string path)
    {
        try { return File.OpenHandle(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete); }
        catch (UnauthorizedAccessException ex) { throw new ReadOnlyFileException(ex); }
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

    internal Task<FileStream> OpenVerifiedReadAsync(string path, FileVersion expected) => Task.Run(() =>
    {
        string target = ResolveSaveTarget(path, expected, createNew: false);
        var stream = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            VerifySaveTarget(path, target, expected, createNew: false);
            VerifyVersion(stream, expected);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    });

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
    public FileEncodingType DetectEncodingType(byte[] bytes) => DetectEncodingType(bytes, preferOem437: false);

    private FileEncodingType DetectEncodingType(byte[] bytes, bool preferOem437)
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

        if (preferOem437) return FileEncodingType.OEM437;

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
            FileEncodingType.OEM437 => Encoding.GetEncoding(437, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback),
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
    LineEndingStyle LineEnding, FileVersion Version)
{
    public bool IsReadOnly { get; init; }
}

public sealed class FileChangedException() : IOException("The file changed or was removed on disk. Use Save As to save your edits to another file.");

internal sealed class ReadOnlyFileException(Exception? innerException = null)
    : IOException("The file is read-only. Use Save As to save your edits to another file.", innerException);
