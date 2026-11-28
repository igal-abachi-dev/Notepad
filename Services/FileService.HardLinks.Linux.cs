using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace NotepadAvalonia.Services;

public partial class FileService
{
    private const int CurrentWorkingDirectory = -100; // AT_FDCWD
    private const int EmptyPath = 0x1000; // AT_EMPTY_PATH
    private const uint LinkFields = 0x104; // STATX_NLINK | STATX_INO

    private static FileLinkInformation? ReadLinuxLinkInformation(string path) =>
        ReadLinuxLinkInformation(CurrentWorkingDirectory, path, flags: 0);

    private static FileLinkInformation ReadLinuxLinkInformation(SafeFileHandle file)
    {
        bool referenced = false;
        try
        {
            file.DangerousAddRef(ref referenced);
            return ReadLinuxLinkInformation(file.DangerousGetHandle().ToInt32(), "", EmptyPath) ?? throw new FileChangedException();
        }
        finally
        {
            if (referenced) file.DangerousRelease();
        }
    }

    private static FileLinkInformation? ReadLinuxLinkInformation(int directory, string path, int flags)
    {
        LinuxStat status;
        try
        {
            if (Statx(directory, path, flags, LinkFields, out status) != 0)
            {
                int error = Marshal.GetLastPInvokeError();
                if (error is 2 or 20) return null; // ENOENT or ENOTDIR
                throw new IOException("Cannot check the file's hard links.", new Win32Exception(error));
            }
        }
        catch (EntryPointNotFoundException ex)
        {
            throw new IOException("This Linux runtime cannot check whether a file has hard links.", ex);
        }
        if ((status.Mask & LinkFields) != LinkFields)
            throw new IOException("The file system did not report the file's hard-link information.");
        ulong device = ((ulong)status.DeviceMajor << 32) | status.DeviceMinor;
        return new(status.LinkCount, device, status.Inode);
    }

    // Linux UAPI struct statx has fixed offsets and room for all fields on each architecture.
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxStat
    {
        [FieldOffset(0)] public uint Mask;
        [FieldOffset(16)] public uint LinkCount;
        [FieldOffset(32)] public ulong Inode;
        [FieldOffset(136)] public uint DeviceMajor;
        [FieldOffset(140)] public uint DeviceMinor;
    }

    [DllImport("libc", EntryPoint = "statx", SetLastError = true)]
    private static extern int Statx(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, out LinuxStat status);
}
