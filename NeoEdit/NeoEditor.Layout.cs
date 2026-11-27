using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Notepad.NeoEdit;

public sealed partial class NeoEditor : ILogicalScrollable
{
    public static readonly StyledProperty<FontFamily> FontFamilyProperty =
        AvaloniaProperty.Register<NeoEditor, FontFamily>(nameof(FontFamily), new FontFamily("Consolas"));
    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<NeoEditor, double>(nameof(FontSize), 14.0);
    public static readonly StyledProperty<FontWeight> FontWeightProperty =
        AvaloniaProperty.Register<NeoEditor, FontWeight>(nameof(FontWeight), FontWeight.Normal);
    public static readonly StyledProperty<FontStyle> FontStyleProperty =
        AvaloniaProperty.Register<NeoEditor, FontStyle>(nameof(FontStyle), FontStyle.Normal);
    public static readonly StyledProperty<IBrush> ForegroundProperty =
        AvaloniaProperty.Register<NeoEditor, IBrush>(nameof(Foreground), Brushes.Black);
    public static readonly StyledProperty<IBrush> BackgroundProperty =
        AvaloniaProperty.Register<NeoEditor, IBrush>(nameof(Background), Brushes.White);
    public static readonly StyledProperty<IBrush> SelectionBrushProperty =
        AvaloniaProperty.Register<NeoEditor, IBrush>(nameof(SelectionBrush), new SolidColorBrush(Color.FromArgb(105, 66, 145, 240)));
    public static readonly StyledProperty<SyntaxLanguage> LanguageProperty =
        AvaloniaProperty.Register<NeoEditor, SyntaxLanguage>(nameof(Language), SyntaxLanguage.Plain);
    public static readonly StyledProperty<bool> WordWrapProperty =
        AvaloniaProperty.Register<NeoEditor, bool>(nameof(WordWrap));
    public static readonly StyledProperty<bool> ShowLineNumbersProperty =
        AvaloniaProperty.Register<NeoEditor, bool>(nameof(ShowLineNumbers), true);
    public static readonly StyledProperty<bool> RightToLeftReadingOrderProperty =
        AvaloniaProperty.Register<NeoEditor, bool>(nameof(RightToLeftReadingOrder));
    public static readonly StyledProperty<bool> ShowUnicodeControlsProperty =
        AvaloniaProperty.Register<NeoEditor, bool>(nameof(ShowUnicodeControls));
    internal static readonly StyledProperty<bool> PreferRightToLeftForNeutralTextProperty =
        AvaloniaProperty.Register<NeoEditor, bool>(nameof(PreferRightToLeftForNeutralText));

    public FontFamily FontFamily { get => GetValue(FontFamilyProperty); set => SetValue(FontFamilyProperty, value); }
    public double FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
    public FontWeight FontWeight { get => GetValue(FontWeightProperty); set => SetValue(FontWeightProperty, value); }
    public FontStyle FontStyle { get => GetValue(FontStyleProperty); set => SetValue(FontStyleProperty, value); }
    public IBrush Foreground { get => GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public IBrush Background { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public IBrush SelectionBrush { get => GetValue(SelectionBrushProperty); set => SetValue(SelectionBrushProperty, value); }
    public SyntaxLanguage Language { get => GetValue(LanguageProperty); set => SetValue(LanguageProperty, value); }
    public bool WordWrap { get => GetValue(WordWrapProperty); set => SetValue(WordWrapProperty, value); }
    public bool ShowLineNumbers { get => GetValue(ShowLineNumbersProperty); set => SetValue(ShowLineNumbersProperty, value); }
    public bool RightToLeftReadingOrder { get => GetValue(RightToLeftReadingOrderProperty); set => SetValue(RightToLeftReadingOrderProperty, value); }
    public bool ShowUnicodeControls { get => GetValue(ShowUnicodeControlsProperty); set => SetValue(ShowUnicodeControlsProperty, value); }
    internal bool PreferRightToLeftForNeutralText { get => GetValue(PreferRightToLeftForNeutralTextProperty); set => SetValue(PreferRightToLeftForNeutralTextProperty, value); }

    private const double Padding = 5;
    private const int TabSize = 4;
    private const int CachedLines = 128;
    private readonly Dictionary<int, LineLayout> _layouts = new();
    private readonly Queue<int> _layoutOrder = new();
    private WrappedLineMetrics? _wrappedMetrics;
    private double _maxWidth;
    private int _widestLine = -1;
    private double _layoutLeft;
    private Vector _offset;
    private IPointer? _selectionPointer;
    private DispatcherTimer? _dragScrollTimer;
    private Point _lastDragPoint;
    private bool _extentUpdateQueued;
    private bool _wrappedRowEnd;

    private sealed record LineLayout(TextLayout Layout, int Length, int[]? TabOffsets, FlowDirection Direction, bool HasBidi,
        List<(int Offset, string Name)>? Controls)
    {
        public int ToVisual(int offset) => TabOffsets?[Math.Clamp(offset, 0, Length)] ?? Math.Clamp(offset, 0, Length);

        public int ToDocument(int offset)
        {
            if (TabOffsets == null) return Math.Clamp(offset, 0, Length);
            int index = Array.BinarySearch(TabOffsets, offset);
            if (index >= 0) return index;
            int next = ~index;
            if (next >= TabOffsets.Length) return Length;
            return offset - TabOffsets[next - 1] < TabOffsets[next] - offset ? next - 1 : next;
        }
    }

    private Typeface Typeface => new(FontFamily, FontStyle, FontWeight);
    private double LineHeight => Math.Max(FontSize * 1.35, 1);
    private double TextLeft => ShowLineNumbers ? Padding * 3 + FontSize * 0.68 * LineCount.ToString().Length : Padding;
    private double WidthForText => Math.Max(1, Bounds.Width - TextLeft - Padding);
    private int PageRows => Math.Max(1, (int)(Bounds.Height / LineHeight) - 1);

    public bool CanHorizontallyScroll { get; set; } = true;
    public bool CanVerticallyScroll { get; set; } = true;
    public bool IsLogicalScrollEnabled => true;
    public Size ScrollSize => new(50, LineHeight);
    public Size PageScrollSize => Viewport;
    public Size Extent { get; private set; }
    public Size Viewport { get; private set; }
    public event EventHandler? ScrollInvalidated;

    public Vector Offset
    {
        get => _offset;
        set
        {
            var clamped = ClampOffset(value);
            if (clamped == _offset) return;
            _offset = clamped;
            InvalidateVisual();
            RaiseScrollInvalidated(EventArgs.Empty);
        }
    }

    public void RaiseScrollInvalidated(EventArgs e) => ScrollInvalidated?.Invoke(this, e);
    public bool BringIntoView(Control target, Rect targetRect) => false;
    public Control? GetControlInDirection(NavigationDirection direction, Control? from) => null;

    private Vector ClampOffset(Vector value) => new(
        WordWrap || !CanHorizontallyScroll ? 0 : Math.Clamp(value.X, 0, Math.Max(0, Extent.Width - Viewport.Width)),
        !CanVerticallyScroll ? 0 : Math.Clamp(value.Y, 0, Math.Max(0, Extent.Height - Viewport.Height)));

    protected override Size MeasureOverride(Size availableSize) => new(
        double.IsFinite(availableSize.Width) ? availableSize.Width : 300,
        double.IsFinite(availableSize.Height) ? availableSize.Height : 300);

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (e.WidthChanged && WordWrap) InvalidateDocumentLayout();
        else UpdateScrollInfo();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == FontFamilyProperty || change.Property == FontSizeProperty ||
            change.Property == FontStyleProperty || change.Property == FontWeightProperty ||
            change.Property == WordWrapProperty || change.Property == ShowLineNumbersProperty ||
            change.Property == ForegroundProperty || change.Property == LanguageProperty ||
            change.Property == RightToLeftReadingOrderProperty || change.Property == PreferRightToLeftForNeutralTextProperty ||
            change.Property == ShowUnicodeControlsProperty || change.Property == ThemeVariantScope.ActualThemeVariantProperty)
        {
            _preferredCaretX = null;
            InvalidateDocumentLayout();
            EnsureCaretVisible();
        }
        else if (change.Property == BackgroundProperty || change.Property == SelectionBrushProperty)
            InvalidateVisual();
    }

    private void InvalidateDocumentLayout(int start = 0, int oldLast = -1, int oldCount = 0)
    {
        int delta = LineCount - oldCount;
        bool gutterChanged = TextLeft != _layoutLeft;
        _layoutLeft = TextLeft;
        if (oldLast < start || gutterChanged)
        {
            foreach (var entry in _layouts.Values) entry.Layout.Dispose();
            _layouts.Clear();
            _layoutOrder.Clear();
            _maxWidth = 0;
            _widestLine = -1;
            _wrappedMetrics = WordWrap ? new(LineCount) : null;
        }
        else
        {
            // Cached layouts are keyed by physical line. Keep unchanged lines and shift the keys after the edit.
            var previousKeys = _layoutOrder.ToArray();
            var previousLayouts = new Dictionary<int, LineLayout>(_layouts);
            _layoutOrder.Clear();
            _layouts.Clear();
            if (_widestLine >= start && _widestLine <= oldLast)
            {
                _widestLine = -1;
                _maxWidth = 0;
            }
            else if (_widestLine > oldLast) _widestLine += delta;
            foreach (int previous in previousKeys)
            {
                var entry = previousLayouts[previous];
                if (previous >= start && previous <= oldLast)
                {
                    entry.Layout.Dispose();
                    continue;
                }
                int line = previous > oldLast ? previous + delta : previous;
                _layoutOrder.Enqueue(line);
                _layouts.Add(line, entry);
                TrackWidth(line, entry);
            }
            _wrappedMetrics?.ApplyEdit(start, oldLast, LineCount);
        }
        // Let the caller update caret geometry against the changed document before publishing the scroll range.
        if (oldLast < 0) UpdateScrollInfo();
        InvalidateVisual();
    }

    private void MeasureViewport()
    {
        if (!WordWrap || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        double bottom = Offset.Y + Bounds.Height + LineHeight;
        for (int line = LineAtY(Offset.Y); line < LineCount && TopOfLine(line) < bottom; line++)
            GetLineLayout(line);
    }

    private void UpdateScrollInfo()
    {
        MeasureViewport();
        var viewport = Bounds.Size;
        var extent = new Size(WordWrap ? viewport.Width : Math.Max(viewport.Width, TextLeft + _maxWidth + Padding),
            Math.Max(viewport.Height, TopOfLine(LineCount)));
        var oldOffset = _offset;
        bool changed = extent != Extent || viewport != Viewport;
        Extent = extent;
        Viewport = viewport;
        _offset = ClampOffset(_offset);
        if (oldOffset != _offset) InvalidateVisual();
        if (changed || oldOffset != _offset) RaiseScrollInvalidated(EventArgs.Empty);
    }

    private double TopOfLine(int line) => _wrappedMetrics?.TopOfLine(line, LineHeight) ?? line * LineHeight;

    private int LineAtY(double y)
    {
        if (_wrappedMetrics == null) return (int)Math.Clamp(Math.Floor(y / LineHeight), 0, LineCount - 1);
        int low = 0;
        int high = LineCount - 1;
        while (low < high)
        {
            int probe = low + (high - low + 1) / 2;
            if (TopOfLine(probe) <= y) low = probe;
            else high = probe - 1;
        }
        return low;
    }

    private LineLayout GetLineLayout(int line)
    {
        if (_layouts.TryGetValue(line, out var entry)) return entry;
        entry = ShapeLine(line);
        if (_layouts.Count >= CachedLines)
        {
            int stale = _layoutOrder.Dequeue();
            _layouts[stale].Layout.Dispose();
            _layouts.Remove(stale);
        }
        _layouts.Add(line, entry);
        _layoutOrder.Enqueue(line);
        _wrappedMetrics?.SetHeight(line, Math.Max(0, entry.Layout.Height - LineHeight));
        TrackWidth(line, entry);
        return entry;
    }

    private void TrackWidth(int line, LineLayout entry)
    {
        if (!WordWrap && entry.Layout.WidthIncludingTrailingWhitespace + 1 > _maxWidth)
        {
            _widestLine = line;
            _maxWidth = entry.Layout.WidthIncludingTrailingWhitespace + 1;
        }
    }

    private LineLayout ShapeLine(int line)
    {
        string text = _doc.GetLineContent(line) ?? "";
        string expanded = text;
        int[]? offsets = null;
        if (text.Contains('\t'))
        {
            offsets = new int[text.Length + 1];
            var builder = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                offsets[i] = builder.Length;
                if (text[i] == '\t')
                    builder.Append(' ', TabSize - builder.Length % TabSize);
                else
                    builder.Append(text[i]);
            }
            offsets[text.Length] = builder.Length;
            expanded = builder.ToString();
        }
        var direction = DirectionOf(text);
        var layout = new TextLayout(expanded, Typeface, FontSize, Foreground,
            textAlignment: WordWrap && direction == FlowDirection.RightToLeft ? TextAlignment.Right : TextAlignment.Left,
            textWrapping: WordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
            flowDirection: direction,
            maxWidth: WordWrap ? WidthForText : double.PositiveInfinity,
            lineHeight: LineHeight,
            textStyleOverrides: SyntaxMatcher.GetOverrides(expanded, Language, Typeface, FontSize, ActualThemeVariant == ThemeVariant.Dark));
        bool hasBidi = direction == FlowDirection.RightToLeft;
        if (!hasBidi)
        {
            foreach (var rune in text.EnumerateRunes())
            {
                if (!IsRtlLetter(rune)) continue;
                hasBidi = true;
                break;
            }
        }
        List<(int, string)>? controls = null;
        if (ShowUnicodeControls)
        {
            for (int i = 0; i < text.Length; i++)
                if (UnicodeControlName(text[i]) is { } name) (controls ??= []).Add((i, name));
        }
        return new LineLayout(layout, text.Length, offsets, direction, hasBidi, controls);
    }

    private FlowDirection DirectionOf(string text)
    {
        if (RightToLeftReadingOrder) return FlowDirection.RightToLeft;
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value is 0x200F or 0x061C) return FlowDirection.RightToLeft;
            if (rune.Value == 0x200E) return FlowDirection.LeftToRight;
            if (!Rune.IsLetter(rune)) continue;
            return IsRtlLetter(rune)
                ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        }
        return PreferRightToLeftForNeutralText ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }

    internal static string? UnicodeControlName(char character) => character switch
    {
        '\0' => "NUL", '\u061C' => "ALM", '\u200B' => "ZWSP", '\u200C' => "ZWNJ", '\u200D' => "ZWJ",
        '\u200E' => "LRM", '\u200F' => "RLM", '\u202A' => "LRE", '\u202B' => "RLE", '\u202C' => "PDF",
        '\u202D' => "LRO", '\u202E' => "RLO", '\u2060' => "WJ", '\u2066' => "LRI", '\u2067' => "RLI",
        '\u2068' => "FSI", '\u2069' => "PDI", '\uFEFF' => "BOM", _ => null
    };

    private static bool IsRtlLetter(Rune rune) => Rune.IsLetter(rune) &&
        rune.Value is >= 0x0590 and <= 0x08FF or >= 0xFB1D and <= 0xFEFC or >= 0x1E800 and <= 0x1EEFF;

    private int HorizontalReadingDirection()
    {
        int line = _doc.GetLineColumnFromOffset(_caretOffset).Item1;
        return GetLineLayout(line).Direction == FlowDirection.RightToLeft ? -1 : 1;
    }

    private int FindHorizontalTarget(int direction)
    {
        var (line, column) = _doc.GetLineColumnFromOffset(_caretOffset);
        var shaped = GetLineLayout(line);
        if (!shaped.HasBidi)
            return TextElementEdge(_caretOffset, direction, advance: true);

        int visual = shaped.ToVisual(column);
        int visualRow = shaped.Layout.GetLineIndexFromCharacterIndex(visual, trailingEdge: _wrappedRowEnd);
        var row = shaped.Layout.TextLines[visualRow];
        double origin = GetCaretHit(shaped, column).X;
        int start = _caretOffset - column;
        // A move can cross from an LTR run into an RTL run. Use the same shaped distances as drawing.
        int from = shaped.ToDocument(row.FirstTextSourceIndex);
        int to = shaped.ToDocument(row.FirstTextSourceIndex + row.Length);
        var text = _doc.GetTextInRange(start + from, to - from);
        int nearest = -1;
        double distance = double.PositiveInfinity;
        for (int index = 0; ; index += StringInfo.GetNextTextElementLength(text.AsSpan(index)))
        {
            int candidate = from + index;
            int position = shaped.ToVisual(candidate);
            if (candidate != column && shaped.Layout.GetLineIndexFromCharacterIndex(position, trailingEdge: false) == visualRow)
            {
                double dx = (shaped.Layout.HitTestTextPosition(position).X - origin) * direction;
                if (dx > 0.01 && dx < distance)
                    (distance, nearest) = (dx, start + candidate);
            }
            if (index >= text.Length) break;
        }
        if (nearest >= 0) return nearest;

        int logical = shaped.Direction == FlowDirection.RightToLeft ? -direction : direction;
        int fallback = TextElementEdge(_caretOffset, logical, advance: true);
        var fallbackLocation = _doc.GetLineColumnFromOffset(fallback);
        return fallbackLocation.Item1 != line ||
            shaped.Layout.GetLineIndexFromCharacterIndex(shaped.ToVisual(fallbackLocation.Item2), trailingEdge: false) != visualRow
            ? fallback : _caretOffset;
    }

    private double LeadingPadding(LineLayout row) =>
        !WordWrap && row.Direction == FlowDirection.RightToLeft
            ? Math.Max(0, WidthForText - row.Layout.WidthIncludingTrailingWhitespace - 1) : 0;

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Background, new Rect(Bounds.Size));
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        MeasureViewport();

        int firstLine = LineAtY(Offset.Y);
        int lastLine = LineAtY(Offset.Y + Bounds.Height);
        int selectionStart = SelectionStart;
        int selectionEnd = selectionStart + SelectionLength;
        var (caretLine, caretColumn) = _doc.GetLineColumnFromOffset(_caretOffset);

        using (context.PushClip(new Rect(TextLeft, 0, Math.Max(0, Bounds.Width - TextLeft), Bounds.Height)))
        {
            for (int line = firstLine; line <= lastLine; line++)
            {
                var row = GetLineLayout(line);
                int start = _doc.GetOffsetFromLineColumn(line, 0);
                var origin = new Point(TextLeft + LeadingPadding(row) - Offset.X, TopOfLine(line) - Offset.Y);
                int from = Math.Max(start, selectionStart);
                int to = Math.Min(start + row.Length, selectionEnd);
                if (from < to)
                {
                    int visualFrom = row.ToVisual(from - start);
                    foreach (var rect in row.Layout.HitTestTextRange(visualFrom, row.ToVisual(to - start) - visualFrom))
                        context.FillRectangle(SelectionBrush, rect.Translate(origin - default(Point)));
                }
                if (selectionStart <= start + row.Length && selectionEnd > start + row.Length)
                {
                    var end = row.Layout.HitTestTextPosition(row.ToVisual(row.Length));
                    context.FillRectangle(SelectionBrush, new Rect(origin.X + end.X, origin.Y + end.Y,
                        Math.Max(3, FontSize / 2), LineHeight));
                }
                row.Layout.Draw(context, origin);
                if (row.Controls != null)
                {
                    foreach (var (offset, name) in row.Controls)
                    {
                        var boundary = row.Layout.HitTestTextPosition(row.ToVisual(offset));
                        var marker = origin + new Vector(boundary.X, boundary.Y);
                        if (marker.X < TextLeft - FontSize * 2 || marker.X >= Bounds.Width ||
                            marker.Y + LineHeight < 0 || marker.Y >= Bounds.Height) continue;
                        using var label = new TextLayout(name, Typeface, Math.Max(6, FontSize * 0.55), Foreground);
                        var box = new Rect(marker, new Size(label.Width + 2, label.Height));
                        context.FillRectangle(Background, box);
                        context.DrawRectangle(SelectionBrush, new Pen(Foreground, 0.5), box);
                        label.Draw(context, marker + new Vector(1, 0));
                    }
                }
                if (line == caretLine && IsFocused && (_caretVisible || _preeditText.Length > 0))
                {
                    var hit = GetCaretHit(row, caretColumn);
                    var top = origin + new Vector(hit.X, hit.Y);
                    if (_preeditText.Length > 0) DrawComposition(context, top);
                    else if (OvertypeMode)
                    {
                        double width = Math.Max(3, FontSize * 0.55);
                        double x = top.X;
                        int next = TextElementEdge(_caretOffset, 1, advance: true) - (_caretOffset - caretColumn);
                        if (next > caretColumn && next <= row.Length && !row.HasBidi)
                        {
                            var edge = row.Layout.HitTestTextPosition(row.ToVisual(next));
                            if (Math.Abs(edge.Y - hit.Y) < 0.01)
                            {
                                x = origin.X + Math.Min(hit.X, edge.X);
                                width = Math.Max(3, Math.Abs(edge.X - hit.X));
                            }
                        }
                        else if (row.Direction == FlowDirection.RightToLeft) x -= width;
                        using (context.PushOpacity(0.48))
                            context.FillRectangle(Foreground, new Rect(x, top.Y, width, LineHeight));
                    }
                    else context.DrawLine(new Pen(Foreground), top, top + new Vector(0, LineHeight));
                }
            }
        }

        if (ShowLineNumbers)
        {
            using (context.PushOpacity(0.56))
            {
                for (int line = firstLine; line <= lastLine; line++)
                {
                    using var number = new TextLayout((line + 1).ToString(), Typeface, FontSize, Foreground, lineHeight: LineHeight);
                    number.Draw(context, new Point(TextLeft - Padding * 2 - number.Width, TopOfLine(line) - Offset.Y));
                }
            }
        }

        bool scrollRangeChanged = WordWrap ? Math.Max(Bounds.Height, TopOfLine(LineCount)) != Extent.Height
            : TextLeft + _maxWidth + Padding > Extent.Width;
        if (scrollRangeChanged && !_extentUpdateQueued)
        {
            _extentUpdateQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _extentUpdateQueued = false;
                UpdateScrollInfo();
            });
        }
    }

    private Rect CaretGeometry()
    {
        var (line, col) = _doc.GetLineColumnFromOffset(_caretOffset);
        var row = GetLineLayout(line);
        var hit = GetCaretHit(row, col);
        return new Rect(LeadingPadding(row) + hit.X, TopOfLine(line) + hit.Y, 1, LineHeight);
    }

    private Rect GetCaretHit(LineLayout row, int offset)
    {
        int position = row.ToVisual(offset);
        if (!_wrappedRowEnd || !WordWrap) return row.Layout.HitTestTextPosition(position);
        int rowIndex = row.Layout.GetLineIndexFromCharacterIndex(position, trailingEdge: true);
        if (rowIndex == row.Layout.GetLineIndexFromCharacterIndex(position, trailingEdge: false))
            return row.Layout.HitTestTextPosition(position);

        var segment = row.Layout.TextLines[rowIndex];
        var previous = segment.GetPreviousCaretCharacterHit(new CharacterHit(position));
        double x = segment.GetDistanceFromCharacterHit(new CharacterHit(previous.FirstCharacterIndex,
            position - previous.FirstCharacterIndex));
        double y = 0;
        for (int i = 0; i < rowIndex; i++) y += row.Layout.TextLines[i].Height;
        return new Rect(x, y, 1, segment.Height);
    }

    private int GetOffsetAt(double x, double y, out bool wrappedRowEnd, out bool insideText)
    {
        int line = LineAtY(y);
        var row = GetLineLayout(line);
        return HitRow(line, row, x, y - TopOfLine(line), out wrappedRowEnd, out insideText);
    }

    private int HitRow(int line, LineLayout row, double x, double rowY, out bool wrappedRowEnd, out bool insideText)
    {
        var hit = row.Layout.HitTestPoint(new Point(x - LeadingPadding(row), Math.Clamp(rowY, 0, row.Layout.Height)));
        insideText = hit.IsInside && rowY >= 0 && rowY < row.Layout.Height;
        int leading = row.Layout.GetLineIndexFromCharacterIndex(hit.TextPosition, trailingEdge: false);
        int trailing = row.Layout.GetLineIndexFromCharacterIndex(hit.TextPosition, trailingEdge: true);
        wrappedRowEnd = leading != trailing && rowY < leading * LineHeight;
        int local = row.ToDocument(hit.TextPosition);
        return SnapOffset(_doc.GetOffsetFromLineColumn(line, local), hit.IsTrailing ? 1 : -1);
    }

    private void EnsureCaretVisible()
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0) return;
        var caret = CaretGeometry();
        UpdateScrollInfo();
        double x = Offset.X;
        double y = Offset.Y;
        if (caret.X < x) x = caret.X;
        else if (caret.Right > x + WidthForText) x = caret.Right - WidthForText;
        if (caret.Y < y) y = caret.Y;
        else if (caret.Bottom > y + Bounds.Height) y = caret.Bottom - Bounds.Height;
        Offset = new Vector(x, y);
    }

    public void ScrollToLine(int line)
    {
        line = Math.Clamp(line, 0, LineCount - 1);
        if (WordWrap) GetLineLayout(line);
        UpdateScrollInfo();
        Offset = new Vector(Offset.X, TopOfLine(line) - Bounds.Height / 2);
    }

    private void MoveVertical(int rows, bool selecting)
    {
        var (line, column) = _doc.GetLineColumnFromOffset(_caretOffset);
        var row = GetLineLayout(line);
        _preferredCaretX ??= LeadingPadding(row) + GetCaretHit(row, column).X;
        int index = row.Layout.GetLineIndexFromCharacterIndex(row.ToVisual(column), trailingEdge: _wrappedRowEnd);
        int target = index + rows;
        while (target < 0 && line > 0)
        {
            row = GetLineLayout(--line);
            target += row.Layout.TextLines.Count;
        }
        while (target >= row.Layout.TextLines.Count && line < LineCount - 1)
        {
            target -= row.Layout.TextLines.Count;
            row = GetLineLayout(++line);
        }
        target = Math.Clamp(target, 0, row.Layout.TextLines.Count - 1);
        double y = 0;
        for (int i = 0; i < target; i++) y += row.Layout.TextLines[i].Height;
        y += row.Layout.TextLines[target].Height / 2;
        int offset = HitRow(line, row, _preferredCaretX.Value, y, out bool rowEnd, out _);
        MoveSelection(selecting ? _anchorOffset : offset, offset, keepPreferredX: true, wrappedRowEnd: rowEnd);
    }

    private void MoveToEdge(bool start, bool selecting, bool document)
    {
        int offset;
        bool rowEnd = false;
        if (document) offset = start ? 0 : _doc.Length;
        else
        {
            var (line, column) = _doc.GetLineColumnFromOffset(_caretOffset);
            var row = GetLineLayout(line);
            int columnAtEdge = start ? 0 : row.Length;
            if (WordWrap && row.Layout.TextLines.Count > 1)
            {
                int visual = row.ToVisual(column);
                int rowIndex = row.Layout.GetLineIndexFromCharacterIndex(visual, trailingEdge: _wrappedRowEnd);
                var segment = row.Layout.TextLines[rowIndex];
                columnAtEdge = row.ToDocument(segment.FirstTextSourceIndex + (start ? 0 : segment.Length));
                rowEnd = !start && rowIndex < row.Layout.TextLines.Count - 1;
            }
            offset = SnapOffset(_doc.GetOffsetFromLineColumn(line, columnAtEdge), start ? -1 : 1);
        }
        MoveSelection(selecting ? _anchorOffset : offset, offset, wrappedRowEnd: rowEnd);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var point = e.GetCurrentPoint(this);
        if (e.Handled || !point.Properties.IsLeftButtonPressed) return;
        _inputMethod.Reset();
        Focus();
        int offset = GetOffsetAt(point.Position.X - TextLeft + Offset.X, point.Position.Y + Offset.Y,
            out bool rowEnd, out bool insideText);
        if (e.KeyModifiers == KeyModifiers.Control && insideText && GetWebLinkAt(offset) is { } link &&
            WebLinkRequested is { } request)
        {
            request(this, link);
            e.Handled = true;
            return;
        }
        _selectionPointer = e.Pointer;
        _lastDragPoint = point.Position;
        e.Pointer.Capture(this);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (!shift && e.ClickCount > 2)
        {
            var (line, _) = _doc.GetLineColumnFromOffset(offset);
            int start = _doc.GetOffsetFromLineColumn(line, 0);
            int end = line + 1 < LineCount ? _doc.GetOffsetFromLineColumn(line + 1, 0) : _doc.Length;
            MoveSelection(start, end);
        }
        else if (!shift && e.ClickCount == 2 && offset < _doc.Length)
        {
            int start = TextElementEdge(offset, -1, advance: false);
            int category = WordCategory(start);
            int end = TextElementEdge(start, 1, advance: true);
            while (start > 0)
            {
                int previous = TextElementEdge(start, -1, advance: true);
                if (WordCategory(previous) != category) break;
                start = previous;
            }
            while (end < _doc.Length && WordCategory(end) == category)
                end = TextElementEdge(end, 1, advance: true);
            MoveSelection(start, end);
        }
        else
            MoveSelection(shift ? _anchorOffset : offset, offset, wrappedRowEnd: rowEnd);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer != _selectionPointer) return;
        _lastDragPoint = e.GetPosition(this);
        UpdateDragSelection();
        if (_lastDragPoint.Y >= 0 && _lastDragPoint.Y <= Bounds.Height &&
            _lastDragPoint.X >= TextLeft && _lastDragPoint.X <= Bounds.Width)
        {
            _dragScrollTimer?.Stop();
            return;
        }
        _dragScrollTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Input, (_, _) =>
        {
            if (_selectionPointer != null) UpdateDragSelection();
        });
        if (!_dragScrollTimer.IsEnabled) _dragScrollTimer.Start();
    }

    private void UpdateDragSelection()
    {
        var p = _lastDragPoint;
        double x = Math.Clamp(p.X, TextLeft, Math.Max(TextLeft, Bounds.Width - 1));
        double y = Math.Clamp(p.Y, 0, Math.Max(0, Bounds.Height - 1));
        Offset += new Vector(Math.Clamp(p.X - x, -50, 50), Math.Clamp(p.Y - y, -3 * LineHeight, 3 * LineHeight));
        int offset = GetOffsetAt(x - TextLeft + Offset.X, y + Offset.Y, out bool rowEnd, out _);
        MoveSelection(_anchorOffset, offset, wrappedRowEnd: rowEnd);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer == _selectionPointer)
        {
            var release = e.GetPosition(this);
            if (release != _lastDragPoint)
            {
                _lastDragPoint = release;
                UpdateDragSelection();
            }
            _selectionPointer = null;
            _dragScrollTimer?.Stop();
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _selectionPointer = null;
        _dragScrollTimer?.Stop();
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (e.Handled) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Delta.Y != 0 && ZoomRequested != null)
        {
            ZoomRequested.Invoke(this, Math.Sign(e.Delta.Y));
            e.Handled = true;
            return;
        }
        var old = Offset;
        bool horizontal = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        Offset -= new Vector((horizontal ? e.Delta.Y : e.Delta.X) * 50, !horizontal ? e.Delta.Y * LineHeight * 3 : 0);
        e.Handled = old != Offset;
    }
}
