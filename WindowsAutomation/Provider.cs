using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Notepad.WindowsAutomation;

[GeneratedComClass]
internal sealed partial class Provider(nint window, EditorDocument document) :
    IRawProvider, IRawFragment, IRawFragmentRoot, ITextProvider2, IValueProvider
{
    private EditorDocument? _document = document;
    private readonly List<WeakReference<TextRange>> _ranges = [];
    private int _rangesCreated;

    internal T Call<T>(Func<EditorDocument, T> action)
    {
        var current = _document ?? throw Disconnected();
        return current.OnUiThread(() => ReferenceEquals(_document, current) ? action(current) : throw Disconnected());
    }

    private static COMException Disconnected() => new("The editor is closed.", Native.Unavailable);

    internal nint Range(TextSpan span)
    {
        var range = new TextRange(this, span);
        if (++_rangesCreated % 64 == 0) _ranges.RemoveAll(reference => !reference.TryGetTarget(out _));
        _ranges.Add(new(range));
        return Native.Pointer<ITextRange>(range);
    }

    internal nint RawPointer() => Native.Pointer<IRawProvider>(this);

    internal void TrackChange(int offset, int removed, int inserted)
    {
        foreach (var reference in _ranges)
            if (reference.TryGetTarget(out var range)) range.TrackChange(offset, removed, inserted);
    }

    internal void ResetRanges()
    {
        foreach (var reference in _ranges)
            if (reference.TryGetTarget(out var range)) range.Reset();
    }

    internal void Disconnect()
    {
        _document = null;
        _ranges.Clear();
        nint pointer = RawPointer();
        try { Native.UiaDisconnectProvider(pointer); }
        finally { Marshal.Release(pointer); }
    }

    public int GetProviderOptions() => 2; // ServerSideProvider

    public nint GetPatternProvider(int patternId) => patternId switch
    {
        10014 => Native.Pointer<ITextProvider>(this),
        10024 => Native.Pointer<ITextProvider2>(this),
        10002 => Native.Pointer<IValueProvider>(this),
        _ => 0
    };

    public void GetPropertyValue(int propertyId, nint variant)
    {
        Call(current =>
        {
            switch (propertyId)
            {
                case 30003: Native.SetNumber(variant, 50004); break; // Edit
                case 30005: Native.SetString(variant, "Document text"); break;
                case 30008: Native.SetBoolean(variant, current.IsFocused); break;
                case 30009: Native.SetBoolean(variant, current.IsEnabled); break;
                case 30010: Native.SetBoolean(variant, current.IsEnabled); break;
                case 30011: Native.SetString(variant, "NeoEditorText"); break;
                case 30016:
                case 30017: Native.SetBoolean(variant, true); break;
                default: Native.SetEmpty(variant); break;
            }
            return 0;
        });
    }

    public nint GetHostRawElementProvider()
    {
        Marshal.ThrowExceptionForHR(Native.UiaHostProviderFromHwnd(window, out var host));
        return host;
    }

    public nint Navigate(int direction) => 0; // The HWND host supplies navigation outside this fragment.
    public nint GetRuntimeId() => 0; // The host identifies this HWND.
    public ScreenRectangle GetBoundingRectangle() => Call(current => current.Bounds);
    public nint GetEmbeddedFragmentRoots() => 0;
    public nint GetFragmentRoot() => Native.Pointer<IRawFragmentRoot>(this);
    public nint GetFocus() => Call(current => current.IsFocused ? Native.Pointer<IRawFragment>(this) : 0);
    public nint ElementProviderFromPoint(double x, double y) => Call(current =>
    {
        var rectangle = current.Bounds;
        return x >= rectangle.Left && y >= rectangle.Top &&
            x < rectangle.Left + rectangle.Width && y < rectangle.Top + rectangle.Height
            ? Native.Pointer<IRawFragment>(this) : 0;
    });

    public void SetFocus() => Call(current => { current.Focus(); return 0; });
    public nint GetSelection() => Call(current => Native.Array(new[] { Range(current.Selection) }));
    public nint GetVisibleRanges() => Call(current => Native.Array(current.GetVisibleRanges().Select(Range).ToArray()));
    public nint RangeFromChild(nint child) => 0;
    public nint RangeFromPoint(UiaPoint point) => Call(current =>
    {
        int offset = current.HitTest(point.X, point.Y);
        return Range(new(offset, offset));
    });
    public nint GetDocumentRange() => Call(current => Range(new(0, current.Length)));
    public int GetSupportedTextSelection() => 1; // Single
    public nint RangeFromAnnotation(nint annotation) => 0;
    public nint GetCaretRange(out int isActive)
    {
        var result = Call(current => (Active: current.IsFocused ? 1 : 0,
            Range: Range(new(current.Caret, current.Caret))));
        isActive = result.Active;
        return result.Range;
    }

    public void SetValue(nint value) =>
        Call(current => { current.SetText(Marshal.PtrToStringUni(value) ?? ""); return 0; });
    public nint GetValue() => Call(current => Marshal.StringToBSTR(current.GetText(new(0, current.Length))));
    public int GetIsReadOnly() => Call(current => current.IsReadOnly ? 1 : 0);
}
