using Microsoft.Win32.SafeHandles;
using NotepadAvalonia.Models;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace NotepadAvalonia.Services;

public partial class FileService
{
    private static string ResolveSaveTarget(string path, FileVersion? expected, bool createNew)
    {
        string? target = createNew ? null : ResolveExistingTarget(path);
        if (expected != null)
        {
            // Older drafts have only the bytes' fingerprint. Require Save As if the path goes through a link.
            bool matches = target != null && (expected.ResolvedPath is { } previous
                ? string.Equals(target, previous, StringComparison.Ordinal)
                : SameLegacyTarget(path, target));
            if (!matches) throw new FileChangedException();
        }
        if (target != null) return target;
        if (!createNew && new FileInfo(path).LinkTarget != null)
            throw new IOException("The symbolic link target is missing or is not a file.");

        string parent = Path.GetDirectoryName(path)!;
        return Path.Combine(ResolveExistingTarget(parent) ?? parent, Path.GetFileName(path));
    }

    private static void VerifySaveTarget(string path, string target, FileVersion? expected, bool createNew)
    {
        if (!string.Equals(ResolveSaveTarget(path, expected, createNew), target, StringComparison.Ordinal))
            throw new FileChangedException();
    }

    private static bool SameLegacyTarget(string path, string target) => OperatingSystem.IsWindows()
        ? string.Equals(WithoutWindowsPrefix(path), WithoutWindowsPrefix(target), StringComparison.OrdinalIgnoreCase)
        : string.Equals(path, target, StringComparison.Ordinal);

    private static string WithoutWindowsPrefix(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + path[8..];
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) && path.Length >= 7 && path[5] == ':') return path[4..];
        return path;
    }

    private static string? ResolveExistingTarget(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            // Access zero requests metadata only. BACKUP_SEMANTICS also permits querying parent directories.
            using var handle = CreateFileW(ToExtendedWindowsPath(path), 0, FileShare.ReadWrite | FileShare.Delete,
                0, FileMode.Open, 0x02000000, 0);
            if (!handle.IsInvalid) return GetWindowsTarget(handle);
            int error = Marshal.GetLastPInvokeError();
            if (error is 2 or 3) return null;
            throw new IOException("Cannot resolve the save path.", new Win32Exception(error));
        }

        nint resolved = RealPath(path, 0);
        if (resolved == 0)
        {
            int error = Marshal.GetLastPInvokeError();
            if (error is 2 or 20) return null; // ENOENT or ENOTDIR
            throw new IOException("Cannot resolve the file path.", new Win32Exception(error));
        }
        try { return Marshal.PtrToStringUTF8(resolved)!; }
        finally { Free(resolved); }
    }

    private static string ToExtendedWindowsPath(string path) => path.Length < 260 ||
        path.StartsWith(@"\\?\", StringComparison.Ordinal) || path.StartsWith(@"\\.\", StringComparison.Ordinal)
        ? path
        : path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;

    private static string GetWindowsTarget(SafeFileHandle handle)
    {
        var path = new StringBuilder(260);
        uint flags = 0;
        while (true)
        {
            uint length = GetFinalPathNameByHandleW(handle, path, (uint)path.Capacity, flags);
            if (length == 0)
            {
                int error = Marshal.GetLastPInvokeError();
                if (error == 5 && flags == 0)
                {
                    flags = 8; // SMB may deny directory-by-directory normalization. Ask the handle for its opened name.
                    continue;
                }
                throw new IOException("Cannot resolve the opened file path.", new Win32Exception(error));
            }
            if (length < path.Capacity) return path.ToString();
            path.EnsureCapacity(checked((int)length));
        }
    }

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint capacity, uint flags);

    [DllImport("libc", EntryPoint = "realpath", SetLastError = true)]
    private static extern nint RealPath([MarshalAs(UnmanagedType.LPUTF8Str)] string path, nint output);

    [DllImport("libc", EntryPoint = "free")]
    private static extern void Free(nint pointer);
}
