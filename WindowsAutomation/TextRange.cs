using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Notepad.WindowsAutomation;

[GeneratedComClass]
internal sealed partial class TextRange(Provider provider, TextSpan span) : ITextRange
{
    private TextSpan _span = span;

    public nint Clone() => provider.Call(_ => provider.Range(_span));

    public int Compare(nint range) => provider.Call(_ => _span == Other(range)._span ? 1 : 0);

    public int CompareEndpoints(int endpoint, nint range, int otherEndpoint) =>
        provider.Call(_ => Endpoint(endpoint).CompareTo(Other(range).Endpoint(otherEndpoint)));

    public void ExpandToEnclosingUnit(TextUnit unit) => provider.Call(document => _span = EnclosingUnit(document, unit));

    public nint FindAttribute(int attribute, NativeVariant value, int backward) =>
        provider.Call(document => attribute == 40015 && value.Type == 11 &&
            (value.Value != 0) == !document.IsEnabled ? provider.Range(_span) : 0);

    public nint FindText(nint text, int backward, int ignoreCase) => provider.Call(document =>
    {
        string query = Marshal.PtrToStringBSTR(text);
        if (query.Length == 0) return 0;
        string content = document.GetText(_span);
        var comparison = ignoreCase != 0 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        int offset = backward != 0 ? content.LastIndexOf(query, comparison) : content.IndexOf(query, comparison);
        return offset < 0 ? 0 : provider.Range(new(_span.Start + offset, _span.Start + offset + query.Length));
    });

    public void GetAttributeValue(int attribute, nint variant)
    {
        provider.Call(document =>
        {
            if (attribute == 40015) Native.SetBoolean(variant, !document.IsEnabled); // IsReadOnly
            else Native.SetUnsupported(variant);
            return 0;
        });
    }

    public nint GetBoundingRectangles() => provider.Call(document =>
        Native.Array(document.GetBounds(_span).SelectMany(rectangle =>
            new[] { rectangle.Left, rectangle.Top, rectangle.Width, rectangle.Height }).ToArray()));

    public nint GetEnclosingElement() => provider.Call(_ => provider.RawPointer());

    public nint GetText(int maximumLength) => provider.Call(document =>
        Marshal.StringToBSTR(document.GetText(maximumLength < 0 ? _span :
            new(_span.Start, _span.Start + Math.Min(maximumLength, _span.Length)))));

    public int Move(TextUnit unit, int count) => provider.Call(document =>
    {
        if (count == 0) return 0;
        var original = _span;
        int start = _span.IsEmpty ? _span.Start : EnclosingUnit(document, unit).Start;
        int moved = Step(document, unit, count, ref start);
        if (original.IsEmpty)
            _span = new(start, start);
        else if (moved != 0)
        {
            // A non-degenerate range moves between units, never to the empty range past the last unit.
            if (start == document.Length && count > 0)
            {
                moved--;
                start = document.UnitAt(start - 1, unit).Start;
            }
            if (moved != 0) _span = document.UnitAt(start, unit);
        }
        return moved;
    });

    public int MoveEndpointByUnit(int endpoint, TextUnit unit, int count) => provider.Call(document =>
    {
        int position = Endpoint(endpoint);
        int moved = Step(document, unit, count, ref position);
        SetEndpoint(endpoint, position);
        return moved;
    });

    public void MoveEndpointByRange(int endpoint, nint range, int otherEndpoint) =>
        provider.Call(_ => { SetEndpoint(endpoint, Other(range).Endpoint(otherEndpoint)); return 0; });

    public void Select() => provider.Call(document => { document.Select(_span); return 0; });

    public void AddToSelection() => throw new InvalidOperationException("The editor supports one selection.");

    public void RemoveFromSelection() => throw new InvalidOperationException("The editor supports one selection.");

    public void ScrollIntoView(int alignToTop) =>
        provider.Call(document => { document.ScrollIntoView(_span, alignToTop != 0); return 0; });

    public nint GetChildren() => provider.Call(_ => Native.Array(Array.Empty<nint>()));

    private TextSpan EnclosingUnit(EditorDocument document, TextUnit unit) =>
        document.Length == 0 ? default :
            document.UnitAt(Math.Min(_span.Start, document.Length - 1), unit);

    private TextRange Other(nint range)
    {
        if (!ComWrappers.TryGetObject(range, out var target) || target is not TextRange other ||
            !ReferenceEquals(other.Owner, provider))
            throw new ArgumentException("The range belongs to a different text provider.");
        return other;
    }

    private Provider Owner => provider;

    private int Endpoint(int endpoint) => endpoint == 0 ? _span.Start : _span.End;

    private void SetEndpoint(int endpoint, int offset) => _span = endpoint == 0
        ? new(offset, Math.Max(offset, _span.End)) : new(Math.Min(_span.Start, offset), offset);

    private static int Step(EditorDocument document, TextUnit unit, int count, ref int offset)
    {
        int moved = 0;
        while (moved != count)
        {
            if (count > 0)
            {
                if (offset >= document.Length) break;
                int next = document.UnitAt(offset, unit).End;
                if (next <= offset) break;
                offset = next;
                moved++;
            }
            else
            {
                if (offset <= 0) break;
                int previous = document.UnitAt(offset - 1, unit).Start;
                if (previous >= offset) break;
                offset = previous;
                moved--;
            }
        }
        return moved;
    }

    internal void TrackChange(int offset, int removed, int inserted)
    {
        int Transform(int position, bool forward)
        {
            if (position < offset || position == offset && !forward) return position;
            if (position >= offset + removed) return position - removed + inserted;
            return offset + (forward ? inserted : 0);
        }
        int start = Transform(_span.Start, forward: true);
        _span = _span.IsEmpty ? new(start, start) : new(start, Math.Max(start, Transform(_span.End, forward: false)));
    }

    internal void Reset() => _span = default;
}
