using Avalonia;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Input.TextInput;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using System;
using System.Diagnostics;

namespace Notepad.NeoEdit;

public sealed partial class NeoEditor
{
    private readonly EditorInputMethod _inputMethod;
    private string _preeditText = "";
    private int _preeditCaret;

    protected override AutomationPeer OnCreateAutomationPeer() => new EditorAutomationPeer(this);

    private sealed class EditorAutomationPeer(NeoEditor editor) : ControlAutomationPeer(editor), IValueProvider
    {
        protected override string GetClassNameCore() => nameof(NeoEditor);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Edit;
        public bool IsReadOnly => !editor.IsEffectivelyEnabled;
        public string Value => editor.GetText();

        public void SetValue(string? value)
        {
            if (IsReadOnly) throw new InvalidOperationException("The editor is disabled.");
            editor.ReplaceRange(0, editor._doc.Length, value ?? "");
        }
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

    private sealed class EditorInputMethod(NeoEditor editor) : TextInputMethodClient
    {
        public override Visual TextViewVisual => editor;
        public override bool SupportsPreedit => true;
        public override bool SupportsSurroundingText => true;

        private (int Start, int End) Context
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

        public override Rect CursorRectangle
        {
            get
            {
                var caret = editor.CaretGeometry().Translate(new Vector(editor.TextLeft - editor.Offset.X, -editor.Offset.Y));
                if (editor._preeditText.Length == 0) return caret;
                using var layout = editor.MakeCompositionLayout();
                var origin = editor.CompositionOrigin(layout, caret.TopLeft);
                var preedit = layout.HitTestTextPosition(editor._preeditCaret);
                return new Rect(origin.X + preedit.X, origin.Y + preedit.Y, 1, editor.LineHeight);
            }
        }

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
