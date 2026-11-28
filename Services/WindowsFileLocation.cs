using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace NotepadAvalonia.Services;

[SupportedOSPlatform("windows")]
internal static class WindowsFileLocation
{
    internal static bool TrySelect(string path)
    {
        if (CoInitializeEx(0, 0) < 0) return false;
        try
        {
            if (SHParseDisplayName(path, 0, out nint item, 0, 0) < 0 || item == 0) return false;
            try
            {
                // With an absolute item ID and no child array, the Shell selects the item in its parent folder.
                return SHOpenFolderAndSelectItems(item, 0, 0, 0) == 0;
            }
            finally { Marshal.FreeCoTaskMem(item); }
        }
        finally { CoUninitialize(); }
    }

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern int CoInitializeEx(nint reserved, uint flags);

    [DllImport("ole32.dll", ExactSpelling = true)]
    private static extern void CoUninitialize();

    [DllImport("shell32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string path, nint bindContext, out nint item, uint attributes, nint reportedAttributes);

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHOpenFolderAndSelectItems(nint item, uint childCount, nint children, uint flags);
}
