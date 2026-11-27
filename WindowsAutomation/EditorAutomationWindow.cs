using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Notepad.WindowsAutomation;

/// <summary>Publishes the editor provider through a window that carries accessibility only.</summary>
[SupportedOSPlatform("windows")]
public sealed class EditorAutomationWindow : IDisposable
{
    private const uint GetObject = 0x003d;
    private const int UiaRootObjectId = -25;
    private const nuint SubclassId = 1;
    private const int FocusChanged = 20005;
    private const int SelectionChanged = 20014;
    private const int TextChanged = 20015;
    private nint _window;
    private bool _notificationsFailed;
    private static volatile bool _listenerCheckFailed;
    private readonly SubclassProc _callback;
    private readonly Provider _provider;

    private EditorAutomationWindow(nint window, EditorDocument document)
    {
        _window = window;
        _provider = new(window, document);
        _callback = OnMessage;
    }

    public static EditorAutomationWindow? TryCreate(nint parent, EditorDocument document)
    {
        const uint transparentLayer = 0x00080000 | 0x00000020 | 0x08000000; // Layered, transparent, no activate
        const uint child = 0x40000000 | 0x10000000; // Child, visible
        nint window = 0;
        EditorAutomationWindow? bridge = null;
        try
        {
            window = CreateWindowExW(transparentLayer, "STATIC", "", child, 0, 0, 0, 0, parent, 0, 0, 0);
            if (window == 0) return null;
            bridge = new(window, document);
            if (!SetLayeredWindowAttributes(window, 0, 0, 2) ||
                !SetWindowSubclass(window, bridge._callback, SubclassId, 0)) return null;

            const int extendedStyle = -20;
            SetWindowLongW(parent, extendedStyle, GetWindowLongW(parent, extendedStyle) | (int)transparentLayer);
            if (!SetLayeredWindowAttributes(parent, 0, 0, 2) ||
                (GetWindowLongW(parent, extendedStyle) & transparentLayer) != transparentLayer) return null;

            window = 0; // The native control host now owns the window.
            return bridge;
        }
        catch (Exception ex) when (AutomationFailure.CanRecover(ex))
        {
            AutomationFailure.Report("host creation", ex);
            return null;
        }
        finally
        {
            if (window != 0)
            {
                DestroyAutomationWindow(window);
                GC.KeepAlive(bridge);
            }
        }
    }

    public nint Handle => _window;

    private nint OnMessage(nint window, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        try
        {
            if (message == GetObject && (int)lParam == UiaRootObjectId && _window != 0)
            {
                nint pointer = _provider.RawPointer();
                try { return Native.UiaReturnRawElementProvider(window, wParam, lParam, pointer); }
                finally { Marshal.Release(pointer); }
            }
            if (message == 0x0084) return -1; // WM_NCHITTEST: HTTRANSPARENT
            if (message == 0x0021) return 3; // WM_MOUSEACTIVATE: MA_NOACTIVATE
            if (message == 0x0082) // WM_NCDESTROY
            {
                _window = 0;
                try
                {
                    RemoveWindowSubclass(window, _callback, SubclassId);
                    Native.UiaReturnRawElementProvider(window, 0, 0, 0);
                }
                finally { _provider.Disconnect(); }
            }
        }
        catch (Exception ex) when (AutomationFailure.CanRecover(ex))
        {
            AutomationFailure.Report("window callback", ex);
        }
        try { return DefSubclassProc(window, message, wParam, lParam); }
        catch (Exception ex) when (AutomationFailure.CanRecover(ex))
        {
            AutomationFailure.Report("default window callback", ex);
            return 0;
        }
    }

    public void NotifyFocus() => Raise(FocusChanged);
    public void NotifySelectionChanged() => Raise(SelectionChanged);
    public void NotifyTextChanged() => Raise(TextChanged);
    public void TrackChange(int offset, int removed, int inserted)
    {
        try { _provider.TrackChange(offset, removed, inserted); }
        catch (Exception ex) when (AutomationFailure.CanRecover(ex)) { DisableRangeTracking(ex); }
    }

    public void ResetRanges()
    {
        try { _provider.ResetRanges(); }
        catch (Exception ex) when (AutomationFailure.CanRecover(ex)) { DisableRangeTracking(ex); }
    }

    private void DisableRangeTracking(Exception exception)
    {
        AutomationFailure.Report("range tracking", exception);
        Dispose();
    }

    internal static bool HasClients()
    {
        if (_listenerCheckFailed) return false;
        try { return Native.UiaClientsAreListening(); }
        catch (Exception ex) when (AutomationFailure.CanRecover(ex))
        {
            _listenerCheckFailed = true;
            AutomationFailure.Report("listener check", ex);
            return false;
        }
    }

    private void Raise(int eventId)
    {
        if (_window == 0 || _notificationsFailed || !HasClients()) return;
        try
        {
            nint pointer = _provider.RawPointer();
            try { Native.UiaRaiseAutomationEvent(pointer, eventId); }
            finally { Marshal.Release(pointer); }
        }
        catch (Exception ex) when (AutomationFailure.CanRecover(ex))
        {
            _notificationsFailed = true;
            AutomationFailure.Report("event notification", ex);
        }
    }

    public void Dispose()
    {
        if (_window != 0) DestroyAutomationWindow(_window);
    }

    private static void DestroyAutomationWindow(nint window)
    {
        try { DestroyWindow(window); }
        catch (Exception ex) when (AutomationFailure.CanRecover(ex))
        {
            AutomationFailure.Report("host destruction", ex);
        }
    }

    private delegate nint SubclassProc(nint window, uint message, nint wParam, nint lParam, nuint id, nuint data);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern nint CreateWindowExW(uint extendedStyle, string windowClass, string text, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint data);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool SetLayeredWindowAttributes(nint window, uint colorKey, byte alpha, uint flags);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int GetWindowLongW(nint window, int index);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern int SetWindowLongW(nint window, int index, int value);
    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool DestroyWindow(nint window);
    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern bool SetWindowSubclass(nint window, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern bool RemoveWindowSubclass(nint window, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll", ExactSpelling = true)]
    private static extern nint DefSubclassProc(nint window, uint message, nint wParam, nint lParam);
}
