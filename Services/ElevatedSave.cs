using NotepadAvalonia.Models;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace NotepadAvalonia.Services;

internal static class ElevatedSave
{
    internal const string WorkerSwitch = "--save-elevated";
    private const string RequestPrefix = "notepad-admin-";
    private const long MaxRequestLength = 1L << 30;
    private static readonly Encoding RequestEncoding = new UTF8Encoding(false, true);

    internal enum WorkerResult { Saved, Failed, Changed, HardLink }

    internal static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    internal static async Task<FileVersion?> SaveAsync(FileService files, string path, string content,
        FileEncodingType encoding, LineEndingStyle lineEnding, FileVersion expected)
    {
        string requestPath = Path.Combine(Path.GetTempPath(), $"{RequestPrefix}{Guid.NewGuid():N}.req");
        bool created = false;
        try
        {
            string seal;
            using (var request = new FileStream(requestPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                created = true;
                seal = await Task.Run(() => WriteRequest(request, path, content, encoding, lineEnding, expected));
            }
            // Hold a read lock until the worker exits. The worker takes the same lock and
            // verifies the seal, including any replacement before it opened the request.
            using var readLock = new FileStream(requestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var written = await Task.Run(() => FingerprintAsync(files.GetEncoding(encoding), content, expected.ResolvedPath!));
            var start = NotepadProcess.CreateStartInfo(Environment.ProcessPath, AppContext.BaseDirectory, WorkerSwitch, requestPath, seal);
            start.UseShellExecute = true;
            start.Verb = "runas";
            WorkerResult result;
            try
            {
                using var worker = await Task.Run(() => Process.Start(start)) ?? throw new IOException("The administrator save worker did not start.");
                await worker.WaitForExitAsync();
                result = (WorkerResult)worker.ExitCode;
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) { return null; }
            catch (Win32Exception ex) { throw new IOException("Cannot start the administrator save worker.", ex); }

            if (result == WorkerResult.Changed) throw new FileChangedException();
            if (result == WorkerResult.HardLink) throw new IOException("This file has multiple hard links. Use Save As or open Notepad as administrator to update all links.");
            if (result != WorkerResult.Saved) throw new IOException("The administrator save could not be completed.");
            using var verification = await files.OpenVerifiedReadAsync(path, written);
            return written;
        }
        finally
        {
            try { if (created) File.Delete(requestPath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    internal static string WriteRequest(FileStream request, string path, string content, FileEncodingType encoding,
        LineEndingStyle lineEnding, FileVersion expected)
    {
        if (string.IsNullOrEmpty(expected.ResolvedPath)) throw new FileChangedException();
        using (var writer = new BinaryWriter(request, RequestEncoding, leaveOpen: true))
        {
            writer.Write(path);
            writer.Write(expected.ResolvedPath);
            writer.Write(expected.Length);
            writer.Write(expected.Sha256);
            writer.Write((int)encoding);
            writer.Write((int)lineEnding);
            writer.Write(content);
        }
        if (request.Length > MaxRequestLength) throw new IOException("The administrator save request is too large.");
        request.Position = 0;
        return Convert.ToHexString(SHA256.HashData(request));
    }

    internal static async Task<WorkerResult> CommitRequestAsync(FileService files, string requestPath, string seal)
    {
        try
        {
            string name = Path.GetFileNameWithoutExtension(requestPath);
            if (seal.Length != 64 || !Path.IsPathFullyQualified(requestPath) || Path.GetExtension(requestPath) != ".req" ||
                !name.StartsWith(RequestPrefix, StringComparison.Ordinal) ||
                !Guid.TryParseExact(name.AsSpan(RequestPrefix.Length), "N", out _)) return WorkerResult.Failed;

            using var request = new FileStream(requestPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (request.Length > MaxRequestLength ||
                !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(seal), SHA256.HashData(request))) return WorkerResult.Failed;
            request.Position = 0;
            using var reader = new BinaryReader(request, RequestEncoding);
            string path = reader.ReadString();
            string target = reader.ReadString();
            long length = reader.ReadInt64();
            string digest = reader.ReadString();
            var encoding = (FileEncodingType)reader.ReadInt32();
            var ending = (LineEndingStyle)reader.ReadInt32();
            if (!Path.IsPathFullyQualified(path) || !Path.IsPathFullyQualified(target) || length < 0 || digest.Length != 64 ||
                !Enum.IsDefined(encoding) || !Enum.IsDefined(ending)) return WorkerResult.Failed;
            string content = reader.ReadString();
            if (request.Position != request.Length) return WorkerResult.Failed;

            var expected = new FileVersion(length, digest) { ResolvedPath = target };
            await files.CommitFileAsync(path, content, encoding, ending, expected);
            return WorkerResult.Saved;
        }
        catch (FileChangedException) { return WorkerResult.Changed; }
        catch (HardLinkSaveRequiredException) { return WorkerResult.HardLink; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException or NotSupportedException)
        {
            return WorkerResult.Failed;
        }
    }

    private static async Task<FileVersion> FingerprintAsync(Encoding encoding, string content, string target)
    {
        using var sha = SHA256.Create();
        using var hashStream = new CryptoStream(Stream.Null, sha, CryptoStreamMode.Write);
        await using (var writer = new StreamWriter(hashStream, encoding, bufferSize: 8192, leaveOpen: true))
            await writer.WriteAsync(content.AsMemory());
        hashStream.FlushFinalBlock();
        long length = (long)encoding.GetByteCount(content) + encoding.GetPreamble().Length;
        return new FileVersion(length, Convert.ToHexString(sha.Hash!)) { ResolvedPath = target };
    }
}
