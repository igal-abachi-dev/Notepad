using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NotepadAvalonia.Services;

internal sealed class WindowsSessionEnd
{
    internal const uint Query = 0x0011;
    internal const uint End = 0x0016;
    private const uint Destroy = 0x0082;
    private const nuint SubclassId = 1;
    private readonly SubclassProc _callback;

    private WindowsSessionEnd(Func<bool> checkpoint, Action confirm)
    {
        _callback = (window, message, wParam, lParam, id, data) =>
        {
            // Avalonia 11 closes from its query handler. Windows may still cancel the
            // request after another application's response. Wait for WM_ENDSESSION.
            if (message == Query) return checkpoint() ? 1 : 0;
            if (message == End && wParam != 0) confirm();
            if (message == Destroy) RemoveWindowSubclass(window, _callback!, id);
            return DefSubclassProc(window, message, wParam, lParam);
        };
    }

    internal static WindowsSessionEnd? TryInstall(Func<bool> checkpoint, Action confirm)
    {
        nint window = FindDispatcherWindow();
        if (window == 0) return null;
        var handler = new WindowsSessionEnd(checkpoint, confirm);
        return SetWindowSubclass(window, handler._callback, SubclassId, 0) ? handler : null;
    }

    internal static nint FindDispatcherWindow()
    {
        nint result = 0;
        var name = new StringBuilder(128);
        EnumThreadWindows(GetCurrentThreadId(), (window, _) =>
        {
            name.Clear();
            if (GetClassNameW(window, name, name.Capacity) == 0 ||
                !name.ToString().StartsWith("AvaloniaMessageWindow ", StringComparison.Ordinal))
                return true;
            result = window;
            return false;
        }, 0);
        return result;
    }

    private delegate nint SubclassProc(nint window, uint message, nint wParam, nint lParam, nuint id, nuint data);
    private delegate bool EnumWindowsProc(nint window, nint data);
    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern bool SetWindowSubclass(nint window, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern bool RemoveWindowSubclass(nint window, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern nint DefSubclassProc(nint window, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool EnumThreadWindows(uint thread, EnumWindowsProc callback, nint data);
    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint window, StringBuilder name, int capacity);
    [DllImport("kernel32.dll", ExactSpelling = true)]
    private static extern uint GetCurrentThreadId();
}
