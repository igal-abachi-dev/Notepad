using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using AutomationFailure = Notepad.WindowsAutomation.AutomationFailure;
using EditorAutomationWindow = Notepad.WindowsAutomation.EditorAutomationWindow;

namespace Notepad.NeoEdit;

public sealed partial class NeoEditor
{
    private readonly EditorInputMethod _inputMethod;
    private string _preeditText = "";
    private int _preeditCaret;

    protected override AutomationPeer OnCreateAutomationPeer() => new EditorAutomationPeer(this);

    private void NotifyAutomationValueChanged()
    {
        try
        {
            if (ControlAutomationPeer.FromElement(this) is EditorAutomationPeer peer)
                peer.NotifyValueChanged();
        }
        catch (Exception ex) when (AutomationFailure.CanRecover(ex))
        {
            AutomationFailure.Report("value notification", ex);
        }
    }

    private sealed class EditorAutomationPeer(NeoEditor editor) : ControlAutomationPeer(editor), IValueProvider
    {
        private DispatcherTimer? _valueTimer;
        private string? _reportedValue;
        private bool _notificationsFailed;

        protected override string GetClassNameCore() => nameof(NeoEditor);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;
        protected override bool IsControlElementCore() => editor.WindowsAutomationHandle == 0 && base.IsControlElementCore();
        protected override bool IsContentElementCore() => editor.WindowsAutomationHandle == 0 && base.IsContentElementCore();
        public bool IsReadOnly => !editor.IsEffectivelyEnabled;
        public string Value
        {
            get
            {
                string text = editor.GetText();
                if (OperatingSystem.IsWindows() && editor.WindowsAutomationHandle == 0 &&
                    EditorAutomationWindow.HasClients()) _reportedValue ??= text;
                return text;
            }
        }

        public void SetValue(string? value)
        {
            if (IsReadOnly) throw new InvalidOperationException("The editor is disabled.");
            editor.ReplaceRange(0, editor._doc.Length, value ?? "");
        }

        public void NotifyValueChanged()
        {
            if (_notificationsFailed) return;
            if (!OperatingSystem.IsWindows() || !EditorAutomationWindow.HasClients())
            {
                _reportedValue = null;
                return;
            }
            if (_valueTimer == null)
            {
                _valueTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                _valueTimer.Tick += (_, _) => PublishValueChange();
            }
            if (!_valueTimer.IsEnabled) _valueTimer.Start();
        }

        private void PublishValueChange()
        {
            try
            {
                _valueTimer!.Stop();
                if (!OperatingSystem.IsWindows() || editor.WindowsAutomationHandle != 0 ||
                    !EditorAutomationWindow.HasClients())
                {
                    _reportedValue = null;
                    return;
                }
                string? previous = _reportedValue;
                _reportedValue = editor.GetText();
                if (!ReferenceEquals(previous, _reportedValue))
                    RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, previous, _reportedValue);
            }
            catch (Exception ex) when (AutomationFailure.CanRecover(ex))
            {
                _reportedValue = null;
                _notificationsFailed = true;
                AutomationFailure.Report("value notification", ex);
            }
        }
    }

    internal nint HandleWindowsImeRequest(nint request, nint data, ref bool handled)
    {
        const int reconvertString = 4; // IMR_RECONVERTSTRING
        const int queryCharacterPosition = 6; // IMR_QUERYCHARPOSITION
        const int documentFeed = 7; // IMR_DOCUMENTFEED
        if (!IsFocused) return 0;
        if (request == reconvertString || request == documentFeed)
        {
            handled = true;
            return FillImeText(data, request == reconvertString);
        }
        if (request != queryCharacterPosition) return 0;
        handled = true;
        if (data == 0 || Marshal.ReadInt32(data) < Marshal.SizeOf<ImeCharacterPosition>()) return 0;
        var position = Marshal.PtrToStructure<ImeCharacterPosition>(data);
        if (position.CharacterOffset > _preeditText.Length) return 0;

        var location = GetCompositionCaret((int)position.CharacterOffset);
        var top = this.PointToScreen(location.TopLeft);
        var bottom = this.PointToScreen(location.BottomLeft);
        var areaStart = this.PointToScreen(default);
        var areaEnd = this.PointToScreen(new Point(Bounds.Width, Bounds.Height));
        position.X = top.X;
        position.Y = top.Y;
        position.LineHeight = (uint)Math.Max(1, bottom.Y - top.Y);
        position.Left = areaStart.X;
        position.Top = areaStart.Y;
        position.Right = areaEnd.X;
        position.Bottom = areaEnd.Y;
        Marshal.StructureToPtr(position, data, fDeleteOld: false);
        return 1;
    }

    private nint FillImeText(nint buffer, bool reconvertSelection)
    {
        // Reconversion starts with an explicit, bounded selection. Ordinary composition
        // still uses Avalonia's native input method and replaces that selection on commit.
        string text;
        int selectionStart;
        int selectionLength;
        if (reconvertSelection)
        {
            if (_preeditText.Length != 0 || SelectionLength is 0 or > 512) return 0;
            text = GetSelectedText();
            if (text.AsSpan().IndexOfAny('\r', '\n', '\0') >= 0) return 0;
            selectionStart = 0;
            selectionLength = text.Length;
        }
        else
        {
            var (start, end) = _inputMethod.Context;
            text = _doc.GetTextInRange(start, end - start);
            if (text.Contains('\0')) return 0;
            selectionStart = Math.Clamp(SelectionStart - start, 0, text.Length);
            selectionLength = Math.Clamp(SelectionStart + SelectionLength - start, selectionStart, text.Length) - selectionStart;
        }

        int headerSize = Marshal.SizeOf<ImeText>();
        int size = headerSize + (text.Length + 1) * sizeof(char);
        if (buffer == 0) return size;
        if (Marshal.ReadInt32(buffer) < size) return 0;
        Marshal.StructureToPtr(new ImeText
        {
            Size = (uint)size,
            TextLength = (uint)text.Length,
            TextOffset = (uint)headerSize,
            CompositionLength = (uint)selectionLength,
            CompositionOffset = (uint)(selectionStart * sizeof(char)),
            TargetLength = (uint)selectionLength,
            TargetOffset = (uint)(selectionStart * sizeof(char))
        }, buffer, fDeleteOld: false);
        Marshal.Copy(text.ToCharArray(), 0, buffer + headerSize, text.Length);
        Marshal.WriteInt16(buffer, size - sizeof(char), 0);
        return size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ImeCharacterPosition
    {
        public uint Size, CharacterOffset;
        public int X, Y;
        public uint LineHeight;
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ImeText
    {
        public uint Size, Version, TextLength, TextOffset;
        public uint CompositionLength, CompositionOffset, TargetLength, TargetOffset;
    }

    private void SetComposition(string? text, int? position = null)
    {
        _preeditText = text ?? "";
        _preeditCaret = Math.Clamp(position ?? _preeditText.Length, 0, _preeditText.Length);
        InvalidateVisual();
    }

    private TextLayout MakeCompositionLayout() => new(_preeditText, Typeface, FontSize, Foreground,
        textDecorations: TextDecorations.Underline, flowDirection: DirectionOf(_preeditText), lineHeight: LineHeight);

    private void DrawComposition(DrawingContext context, Point origin)
    {
        using var layout = MakeCompositionLayout();
        origin = CompositionOrigin(layout, origin);
        context.FillRectangle(Background, new Rect(origin, new Size(layout.WidthIncludingTrailingWhitespace + 1, LineHeight)));
        layout.Draw(context, origin);
        var caret = layout.HitTestTextPosition(_preeditCaret);
        var top = origin + new Vector(caret.X, caret.Y);
        context.DrawLine(new Pen(Foreground), top, top + new Vector(0, LineHeight));
    }

    private Point CompositionOrigin(TextLayout layout, Point origin) =>
        origin.WithX(Math.Max(TextLeft, Math.Min(origin.X, Bounds.Width - layout.WidthIncludingTrailingWhitespace - Padding)));

    private Rect GetCompositionCaret(int characterOffset)
    {
        var caret = CaretGeometry().Translate(new Vector(TextLeft - Offset.X, -Offset.Y));
        if (_preeditText.Length == 0) return caret;
        using var layout = MakeCompositionLayout();
        var origin = CompositionOrigin(layout, caret.TopLeft);
        var preedit = layout.HitTestTextPosition(characterOffset);
        return new Rect(origin.X + preedit.X, origin.Y + preedit.Y, 1, LineHeight);
    }

    private sealed class EditorInputMethod(NeoEditor editor) : TextInputMethodClient
    {
        public override Visual TextViewVisual => editor;
        public override bool SupportsPreedit => true;
        public override bool SupportsSurroundingText => true;

        public (int Start, int End) Context
        {
            get
            {
                const int contextLength = 512;
                int caret = editor.CaretOffset;
                int start = editor.SnapOffset(Math.Max(0, caret - contextLength), -1);
                int end = editor.SnapOffset(caret + Math.Min(editor._doc.Length - caret, contextLength), 1);
                return (start, end);
            }
        }

        public override string SurroundingText
        {
            get
            {
                var (start, end) = Context;
                return editor._doc.GetTextInRange(start, end - start);
            }
        }

        public override TextSelection Selection
        {
            get
            {
                var (start, end) = Context;
                return new(Math.Clamp(editor._anchorOffset, start, end) - start, editor.CaretOffset - start);
            }
            set
            {
                var (start, end) = Context;
                int anchor = Math.Clamp(value.Start, 0, end - start);
                int caret = Math.Clamp(value.End, 0, end - start);
                editor.SetSelection(start + anchor, caret - anchor);
            }
        }

        public override Rect CursorRectangle => editor.GetCompositionCaret(editor._preeditCaret);

        public override void SetPreeditText(string? text) => SetPreeditText(text, null);

        public override void SetPreeditText(string? text, int? cursor)
        {
            editor.SetComposition(text, cursor);
            RaiseCursorRectangleChanged();
        }

        public void Reset()
        {
            if (editor._preeditText.Length == 0) return;
            editor.SetComposition(null);
            RequestReset();
        }

        public void NotifySelectionAndText()
        {
            RaiseSurroundingTextChanged();
            RaiseSelectionChanged();
            RaiseCursorRectangleChanged();
        }

        public void NotifyGeometry() => RaiseCursorRectangleChanged();

        public override async void ExecuteContextMenuAction(ContextMenuAction action)
        {
            try
            {
                switch (action)
                {
                    case ContextMenuAction.Copy: await editor.CopyAsync(); break;
                    case ContextMenuAction.Cut: await editor.CutAsync(); break;
                    case ContextMenuAction.Paste: await editor.PasteAsync(); break;
                    case ContextMenuAction.SelectAll: editor.SelectAll(); break;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                Trace.TraceWarning("Text input clipboard operation failed: {0}", ex.GetType().Name);
            }
        }
    }
}
