using Microsoft.Win32.SafeHandles;
using NotepadAvalonia.Models;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;

namespace NotepadAvalonia.Services;

public partial class FileService
{
    private static bool CheckHardLinks(string path, FileStream? versionCheckedFile, FileVersion? version)
    {
        if (versionCheckedFile != null)
        {
            var info = ReadLinkInformation(versionCheckedFile.SafeFileHandle);
            if (info.LinkCount > 1)
                throw new HardLinkSaveRequiredException(new(info.Volume, info.Index, version!));
            return true;
        }

        // Save As may target a file for which we have never read the contents.
        if (ReadLinkInformation(path) is not { } observed) return false;
        if (observed.LinkCount <= 1) return true;

        using var file = OpenForVersionCheck(path);
        var current = ReadLinkInformation(file.SafeFileHandle);
        if (current != observed) throw new FileChangedException();
        throw new HardLinkSaveRequiredException(new(current.Volume, current.Index, ReadVersion(file)));
    }

    private static void CommitHardLinkedFile(string staging, string path, FileVersion? expected, HardLinkSaveTarget approved)
    {
        if (!(OperatingSystem.IsWindows() || OperatingSystem.IsLinux()))
            throw new IOException("Updating all hard links is supported on Windows and Linux.");

        using var data = File.OpenRead(staging);
        using var target = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        // The exclusive file open applies across all aliases. On Linux the lock is advisory.
        var info = ReadLinkInformation(target.SafeFileHandle);
        if (info.LinkCount <= 1 || info.Volume != approved.Volume || info.Index != approved.Index ||
            (expected != null && expected != approved.Version))
            throw new FileChangedException();
        VerifyVersion(target, approved.Version);
        WriteWithRecoveryCopy(target, data, VerifyLinuxTarget);

        void VerifyLinuxTarget()
        {
            if (!OperatingSystem.IsLinux()) return;
            if (ReadLinuxLinkInformation(path) != info) throw new FileChangedException();
            if ((File.GetUnixFileMode(target.SafeFileHandle) & UnixWriteBits) == 0) throw new ReadOnlyFileException();
        }
    }

    internal static void WriteWithRecoveryCopy(FileStream target, Stream data, Action? beforeWrite = null)
    {
        string directory = Path.GetDirectoryName(target.Name)!;
        string filename = Path.GetFileName(target.Name);
        string label = filename.Length <= 180 ? filename + "-" : "";
        string backup = Path.Combine(directory, $".notepad-recovery-{label}{Guid.NewGuid():N}.bak");
        bool created = false;
        bool backupReady = false;
        bool committed = false;
        try
        {
            using (var recovery = CreateRecoveryCopy(target, backup))
            {
                created = true;
                target.Position = 0;
                target.CopyTo(recovery);
                recovery.Flush(flushToDisk: true);
            }
            beforeWrite?.Invoke();
            backupReady = true;

            target.Position = 0;
            data.CopyTo(target);
            target.SetLength(data.Length);
            target.Flush(flushToDisk: true);
            committed = true;
        }
        catch (Exception ex) when (backupReady && ex is (IOException or UnauthorizedAccessException))
        {
            throw new IOException($"The linked file could not be fully saved. The original contents are in:\n{backup}", ex);
        }
        finally
        {
            if (created && (!backupReady || committed)) DeleteSaveArtifact(backup);
        }
    }

    private static FileStream CreateRecoveryCopy(FileStream target, string path)
    {
        if (!OperatingSystem.IsWindows())
            return new FileStream(path, new FileStreamOptions
            {
                Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
            });

        // A copy next to a restricted file must not inherit wider access from the folder.
        var access = new FileSecurity();
        access.SetSecurityDescriptorBinaryForm(target.GetAccessControl().GetSecurityDescriptorBinaryForm(),
            AccessControlSections.Access);
        access.SetAccessRuleProtection(isProtected: true, preserveInheritance: true);
        return new FileInfo(path).Create(FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096,
            FileOptions.None, access);
    }

    private static FileLinkInformation? ReadLinkInformation(string path)
    {
        if (OperatingSystem.IsLinux()) return ReadLinuxLinkInformation(path);
        using var handle = CreateFileW(ToExtendedWindowsPath(path), 0, FileShare.ReadWrite | FileShare.Delete, 0, FileMode.Open, 0, 0);
        if (!handle.IsInvalid) return ReadLinkInformation(handle);
        int error = Marshal.GetLastPInvokeError();
        if (error is 2 or 3) return null;
        throw new IOException("Cannot check the file's hard links.", new Win32Exception(error));
    }

    private static FileLinkInformation ReadLinkInformation(SafeFileHandle file)
    {
        if (OperatingSystem.IsLinux()) return ReadLinuxLinkInformation(file);
        if (!GetFileInformationByHandle(file, out var info))
            throw new IOException("Cannot check the file's hard links.", new Win32Exception(Marshal.GetLastWin32Error()));
        return new(info.LinkCount, info.VolumeSerial, ((ulong)info.IndexHigh << 32) | info.IndexLow);
    }

    private readonly record struct FileLinkInformation(uint LinkCount, ulong Volume, ulong Index);

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME Creation, LastAccess, LastWrite;
        public uint VolumeSerial, SizeHigh, SizeLow, LinkCount, IndexHigh, IndexLow;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, FileShare share, nint security,
        FileMode disposition, uint flags, nint template);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation info);
}

internal readonly record struct HardLinkSaveTarget(ulong Volume, ulong Index, FileVersion Version);

internal sealed class HardLinkSaveRequiredException(HardLinkSaveTarget target)
    : IOException("This file has multiple hard links.")
{
    internal HardLinkSaveTarget Target { get; } = target;
}
