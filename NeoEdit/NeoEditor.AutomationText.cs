using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation.Peers;
using Avalonia.Platform;
using Avalonia.Threading;
using Notepad.WindowsAutomation;
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;

namespace Notepad.NeoEdit;

public sealed partial class NeoEditor
{
    private EditorAutomationWindow? _automationWindow;
    private NativeControlHost? _automationHost;
    private (int Anchor, int Caret) _automationSelection;

    internal nint WindowsAutomationHandle => OperatingSystem.IsWindows() && _automationHost != null
        ? _automationWindow?.Handle ?? 0 : 0;

    [SupportedOSPlatform("windows")]
    internal void AttachWindowsTextAutomation(Window owner, Panel surface)
    {
        var document = new AutomationDocument(this);
        try
        {
            _automationHost = new AutomationHost(this, document) { IsHitTestVisible = false };
            surface.Children.Add(_automationHost);
        }
        catch (Exception ex) when (AutomationFailure.CanRecover(ex))
        {
            DisableAutomationHost("editor attachment", ex);
            return;
        }
        owner.Activated += (_, _) => NotifyFocus();
        GotFocus += (_, _) => NotifyFocus();

        void NotifyFocus()
        {
            if (IsFocused)
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsFocused && owner.IsActive && WindowsAutomationHandle != 0)
                        _automationWindow?.NotifyFocus();
                }, DispatcherPriority.Input);
        }
    }

    internal AutomationPeer? GetHostedTextPeer()
    {
        if (WindowsAutomationHandle == 0 || _automationHost == null) return null;
        try
        {
            var peers = ControlAutomationPeer.CreatePeerForElement(_automationHost)?.GetChildren();
            return peers is { Count: > 0 } ? peers[0] : null;
        }
        catch (Exception ex) when (AutomationFailure.CanRecover(ex))
        {
            DisableAutomationHost("editor peer lookup", ex);
            return null;
        }
    }

    private void DisableAutomationHost(string operation, Exception exception)
    {
        var host = _automationHost;
        _automationHost = null;
        AutomationFailure.Report(operation, exception);
        if (host != null) Dispatcher.UIThread.Post(() => HideAutomationHost(host));
    }

    private static void HideAutomationHost(NativeControlHost host)
    {
        try { host.IsVisible = false; }
        catch (Exception ex) when (AutomationFailure.CanRecover(ex))
        {
            AutomationFailure.Report("host visibility", ex);
        }
    }

    [SupportedOSPlatform("windows")]
    private sealed class AutomationHost(NeoEditor editor, EditorDocument document) : NativeControlHost
    {
        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            editor._automationWindow = EditorAutomationWindow.TryCreate(parent.Handle, document);
            if (editor._automationWindow is { } window) return new PlatformHandle(window.Handle, "HWND");
            Dispatcher.UIThread.Post(() => HideAutomationHost(this));
            return base.CreateNativeControlCore(parent);
        }

        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            if (editor._automationWindow is { } window)
            {
                window.Dispose();
                editor._automationWindow = null;
            }
            else base.DestroyNativeControlCore(control);
        }
    }

    private void NotifyNativeAutomationSelection()
    {
        var selection = (_anchorOffset, _caretOffset);
        if (selection == _automationSelection) return;
        _automationSelection = selection;
        if (OperatingSystem.IsWindows() && WindowsAutomationHandle != 0)
            _automationWindow?.NotifySelectionChanged();
    }

    private void NotifyAutomationTextChanged()
    {
        if (OperatingSystem.IsWindows() && WindowsAutomationHandle != 0)
            _automationWindow?.NotifyTextChanged();
        else
            NotifyAutomationValueChanged();
    }

    internal EditorDocument CreateAutomationDocument() => new AutomationDocument(this);

    private sealed class AutomationDocument(NeoEditor editor) : EditorDocument
    {
        public override T OnUiThread<T>(Func<T> action) =>
            Dispatcher.UIThread.CheckAccess() ? action() : Dispatcher.UIThread.Invoke(action);
        public override int Length => editor.TextLength;
        public override bool IsFocused => editor.IsFocused && TopLevel.GetTopLevel(editor) is Window { IsActive: true };
        public override bool IsEnabled => editor.IsEffectivelyEnabled;
        public override bool IsReadOnly => editor.IsReadOnly || !IsEnabled;
        public override TextSpan Selection => new(editor.SelectionStart, editor.SelectionStart + editor.SelectionLength);
        public override int Caret => editor.CaretOffset;
        public override string GetText(TextSpan range) => editor._doc.GetTextInRange(range.Start, range.Length);
        public override ScreenRectangle Bounds => ToScreen(new Rect(editor.Bounds.Size));

        public override TextSpan UnitAt(int offset, TextUnit unit)
        {
            if (Length == 0) return default;
            offset = Math.Clamp(offset, 0, Length - 1);
            if (unit is TextUnit.Page or TextUnit.Document) return new(0, Length);
            if (unit is TextUnit.Character or TextUnit.Word or TextUnit.Format)
            {
                int start = editor.TextElementEdge(editor.SnapOffset(offset, -1), -1, advance: false);
                int end = editor.TextElementEdge(start, 1, advance: true);
                return unit == TextUnit.Character ? new(start, end) : WordAt(start, end);
            }

            var (line, column) = editor._doc.GetLineColumnFromOffset(offset);
            int lineStart = editor._doc.GetOffsetFromLineColumn(line, 0);
            int nextLine = line + 1 < editor.LineCount ? editor._doc.GetOffsetFromLineColumn(line + 1, 0) : Length;
            if (unit == TextUnit.Paragraph || !editor.WordWrap) return new(lineStart, nextLine);
            var layout = editor.GetLineLayout(line);
            int index = layout.Layout.GetLineIndexFromCharacterIndex(layout.ToVisual(column), trailingEdge: false);
            var row = layout.Layout.TextLines[index];
            int rowStart = lineStart + layout.ToDocument(row.FirstTextSourceIndex);
            int rowEnd = index + 1 == layout.Layout.TextLines.Count
                ? nextLine : lineStart + layout.ToDocument(row.FirstTextSourceIndex + row.Length);
            return new(rowStart, rowEnd);
        }

        private TextSpan WordAt(int start, int end)
        {
            var (line, column) = editor._doc.GetLineColumnFromOffset(start);
            int lineStart = start - column;
            int lineEnd = lineStart + editor._doc.GetLineContentLength(line);
            if (start >= lineEnd) return new(start, end); // CR, LF, or CRLF
            int category = editor.WordCategory(start);
            bool trailingSpace = category == 0;
            while (start > lineStart)
            {
                int previous = editor.TextElementEdge(start, -1, advance: true);
                int previousCategory = editor.WordCategory(previous);
                if (category == 0) category = previousCategory;
                if (previousCategory != category) break;
                start = previous;
            }
            while (!trailingSpace && end < lineEnd && editor.WordCategory(end) == category)
                end = editor.TextElementEdge(end, 1, advance: true);
            while (end < lineEnd && editor.WordCategory(end) == 0)
                end = editor.TextElementEdge(end, 1, advance: true);
            return new(start, end);
        }

        public override TextSpan[] GetVisibleRanges()
        {
            if (editor.Bounds.Height <= 0) return [];
            if (Length == 0) return [default];
            int first = editor.LineAtY(editor.Offset.Y);
            int last = editor.LineAtY(editor.Offset.Y + editor.Bounds.Height);
            var result = new List<TextSpan>();
            for (int line = first; line <= last; line++)
            {
                var row = editor.GetLineLayout(line);
                double y = editor.TopOfLine(line) - editor.Offset.Y;
                foreach (var segment in row.Layout.TextLines)
                {
                    if (y >= editor.Bounds.Height) break;
                    if (y + segment.Height > 0)
                    {
                        double middle = y + editor.Offset.Y - editor.TopOfLine(line) + segment.Height / 2;
                        int left = editor.HitRow(line, row, editor.Offset.X, middle, out _, out _);
                        int right = editor.HitRow(line, row, editor.Offset.X + editor.WidthForText, middle, out _, out _);
                        var span = new TextSpan(Math.Min(left, right), Math.Max(left, right));
                        if (result.Count != 0 && span.Start <= result[^1].End)
                            result[^1] = new(Math.Min(result[^1].Start, span.Start), Math.Max(result[^1].End, span.End));
                        else result.Add(span);
                    }
                    y += segment.Height;
                }
            }
            return result.ToArray();
        }

        public override ScreenRectangle[] GetBounds(TextSpan range)
        {
            if (range.IsEmpty || editor.Bounds.Height <= 0) return [];
            int first = Math.Max(editor.LineAtY(editor.Offset.Y),
                editor._doc.GetLineColumnFromOffset(range.Start).Item1);
            int last = Math.Min(editor.LineAtY(editor.Offset.Y + editor.Bounds.Height),
                editor._doc.GetLineColumnFromOffset(range.End).Item1);
            var viewport = new Rect(editor.TextLeft, 0, editor.WidthForText, editor.Bounds.Height);
            var bounds = new List<ScreenRectangle>();
            for (int line = first; line <= last; line++)
            {
                int start = editor._doc.GetOffsetFromLineColumn(line, 0);
                var row = editor.GetLineLayout(line);
                int from = row.ToVisual(Math.Max(range.Start, start) - start);
                int to = row.ToVisual(range.End - start);
                if (from >= to) continue;
                var origin = new Vector(editor.TextLeft + editor.LeadingPadding(row) - editor.Offset.X,
                    editor.TopOfLine(line) - editor.Offset.Y);
                foreach (var rect in row.Layout.HitTestTextRange(from, to - from))
                {
                    var clipped = rect.Translate(origin).Intersect(viewport);
                    if (clipped.Width > 0 && clipped.Height > 0) bounds.Add(ToScreen(clipped));
                }
            }
            return bounds.ToArray();
        }

        private ScreenRectangle ToScreen(Rect rectangle)
        {
            if (TopLevel.GetTopLevel(editor) == null) return default;
            var left = editor.PointToScreen(rectangle.TopLeft);
            var right = editor.PointToScreen(rectangle.BottomRight);
            return new(left.X, left.Y, right.X - left.X, right.Y - left.Y);
        }

        public override int HitTest(double x, double y)
        {
            var point = editor.PointToClient(new PixelPoint((int)x, (int)y));
            return editor.GetOffsetAt(point.X - editor.TextLeft + editor.Offset.X, point.Y + editor.Offset.Y,
                out _, out _);
        }

        public override void Select(TextSpan range) => editor.SetSelection(range.Start, range.Length);

        public override void ScrollIntoView(TextSpan range, bool alignToTop)
        {
            var (line, column) = editor._doc.GetLineColumnFromOffset(alignToTop || range.IsEmpty ? range.Start : range.End - 1);
            var row = editor.GetLineLayout(line);
            var hit = row.Layout.HitTestTextPosition(row.ToVisual(column));
            editor.UpdateScrollInfo();
            double x = editor.Offset.X;
            double left = editor.LeadingPadding(row) + hit.X;
            if (left < x) x = left;
            else if (left > x + editor.WidthForText) x = left - editor.WidthForText;
            double y = editor.TopOfLine(line) + hit.Y;
            editor.Offset = new Vector(x, alignToTop ? y : y + editor.LineHeight - editor.Bounds.Height);
        }

        public override void Focus()
        {
            if (TopLevel.GetTopLevel(editor) is Window { IsActive: false } window) window.Activate();
            editor.Focus();
        }

        public override void SetText(string value)
        {
            if (IsReadOnly) throw new InvalidOperationException("The editor cannot accept text.");
            editor.ReplaceRange(0, Length, value);
        }
    }
}
