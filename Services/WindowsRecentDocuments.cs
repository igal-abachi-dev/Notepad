using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace NotepadAvalonia.Services;

internal static class WindowsRecentDocuments
{
    private const string AppId = "NeoEdit.Notepad";
    private const uint AppIdAndLink = 7; // SHARD_APPIDINFOLINK
    private static readonly Guid ShellLinkClass = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid ShellLinkInterface = new("000214F9-0000-0000-C000-000000000046");
    private static readonly Guid PropertyStoreInterface = new("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
    private static readonly PropertyKey Title = new(new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2);
    private static bool _registered;

    internal static void RegisterApplication() =>
        _registered = SetCurrentProcessExplicitAppUserModelID(AppId) >= 0;

    internal static void Add(string path)
    {
        if (!_registered) return;
        try { BuildRecentLink(path, publish: true); }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException or ArgumentException
            or IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Could not add a document to the Windows Jump List: {0:X8}", ex.HResult);
        }
    }

    internal static void VerifyLink(string path) => BuildRecentLink(path, publish: false);

    private static void BuildRecentLink(string path, bool publish)
    {
        var launcher = NotepadProcess.CreateStartInfo(Environment.ProcessPath, AppContext.BaseDirectory, "--", path);
        // All arguments are a literal separator or Windows file paths, which cannot contain quotes.
        string arguments = string.Join(" ", launcher.ArgumentList.Select(value => "\"" + value + "\""));
        Marshal.ThrowExceptionForHR(CoInitializeEx(0, 2)); // COINIT_APARTMENTTHREADED
        try
        {
            Marshal.ThrowExceptionForHR(CoCreateInstance(in ShellLinkClass, 0, 1, in ShellLinkInterface, out nint link));
            try
            {
                // Slots from IShellLinkW, including its inherited IUnknown methods.
                Marshal.ThrowExceptionForHR(Method<SetLinkText>(link, 20)(link, launcher.FileName)); // SetPath
                Marshal.ThrowExceptionForHR(Method<SetLinkText>(link, 11)(link, arguments)); // SetArguments
                Marshal.ThrowExceptionForHR(Method<SetLinkIcon>(link, 17)(link, launcher.FileName, 0)); // SetIconLocation
                SetTitle(link, Path.GetFileName(path));
                if (publish)
                {
                    var recent = new RecentLink(link, AppId);
                    SHAddToRecentDocs(AppIdAndLink, in recent);
                }
            }
            finally { Marshal.Release(link); }
        }
        finally { CoUninitialize(); }
    }

    private static void SetTitle(nint link, string title)
    {
        var iid = PropertyStoreInterface;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(link, in iid, out nint store));
        try
        {
            var value = new PropertyValue(31, 0, 0, 0, Marshal.StringToCoTaskMemUni(title), 0); // VT_LPWSTR
            try
            {
                Marshal.ThrowExceptionForHR(Method<SetProperty>(store, 6)(store, in Title, in value)); // SetValue
                Marshal.ThrowExceptionForHR(Method<CommitProperties>(store, 7)(store)); // Commit
            }
            finally { Marshal.FreeCoTaskMem(value.Pointer); }
        }
        finally { Marshal.Release(store); }
    }

    private static T Method<T>(nint instance, int slot) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct PropertyKey(Guid Format, uint Id);

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct PropertyValue(ushort Type, ushort Reserved1, ushort Reserved2, ushort Reserved3,
        nint Pointer, nint Padding);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private readonly record struct RecentLink(nint Link, string AppId);

    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int SetLinkText(nint self, string value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Unicode)]
    private delegate int SetLinkIcon(nint self, string path, int index);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int SetProperty(nint self, in PropertyKey key, in PropertyValue value);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate int CommitProperties(nint self);

    [DllImport("shell32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string id);
    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern void SHAddToRecentDocs(uint kind, in RecentLink recent);
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();
    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint instance);
}
