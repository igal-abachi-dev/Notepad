using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Notepad.WindowsAutomation;

[StructLayout(LayoutKind.Sequential)]
internal readonly record struct UiaPoint(double X, double Y);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct ScreenRectangle(double Left, double Top, double Width, double Height);

[GeneratedComInterface]
[Guid("d6dd68d1-86fd-4332-8666-9abedea2d24c")]
internal partial interface IRawProvider
{
    int GetProviderOptions();
    nint GetPatternProvider(int patternId);
    void GetPropertyValue(int propertyId, nint variant);
    nint GetHostRawElementProvider();
}

[GeneratedComInterface]
[Guid("fcf5d820-d7ec-4613-bdf6-42a84ce7daaf")]
internal partial interface IRawFragment
{
    nint Navigate(int direction);
    nint GetRuntimeId();
    ScreenRectangle GetBoundingRectangle();
    nint GetEmbeddedFragmentRoots();
    void SetFocus();
    nint GetFragmentRoot();
}

[GeneratedComInterface]
[Guid("620ce2a5-ab8f-40a9-86cb-de3c75599b58")]
internal partial interface IRawFragmentRoot
{
    nint ElementProviderFromPoint(double x, double y);
    nint GetFocus();
}

[GeneratedComInterface]
[Guid("c7935180-6fb3-4201-b174-7df73adbf64a")]
internal partial interface IValueProvider
{
    void SetValue(nint value);
    nint GetValue();
    int GetIsReadOnly();
}

[GeneratedComInterface]
[Guid("3589c92c-63f3-4367-99bb-ada653b77cf2")]
internal partial interface ITextProvider
{
    nint GetSelection();
    nint GetVisibleRanges();
    nint RangeFromChild(nint child);
    nint RangeFromPoint(UiaPoint point);
    nint GetDocumentRange();
    int GetSupportedTextSelection();
}

[GeneratedComInterface]
[Guid("0dc5e6ed-3e16-4bf1-8f9a-a979878bc195")]
internal partial interface ITextProvider2 : ITextProvider
{
    nint RangeFromAnnotation(nint annotation);
    nint GetCaretRange(out int isActive);
}

[GeneratedComInterface]
[Guid("5347ad7b-c355-46f8-aff5-909033582f63")]
internal partial interface ITextRange
{
    nint Clone();
    int Compare(nint range);
    int CompareEndpoints(int endpoint, nint range, int otherEndpoint);
    void ExpandToEnclosingUnit(TextUnit unit);
    nint FindAttribute(int attribute, NativeVariant value, int backward);
    nint FindText(nint text, int backward, int ignoreCase);
    void GetAttributeValue(int attribute, nint variant);
    nint GetBoundingRectangles();
    nint GetEnclosingElement();
    nint GetText(int maximumLength);
    int Move(TextUnit unit, int count);
    int MoveEndpointByUnit(int endpoint, TextUnit unit, int count);
    void MoveEndpointByRange(int endpoint, nint range, int otherEndpoint);
    void Select();
    void AddToSelection();
    void RemoveFromSelection();
    void ScrollIntoView(int alignToTop);
    nint GetChildren();
}

// VARIANT contains two pointers in its largest payload, even when passed by value.
[StructLayout(LayoutKind.Sequential)]
internal struct NativeVariant
{
    public ushort Type, Reserved1, Reserved2, Reserved3;
    public nint Value, Extra;
}
