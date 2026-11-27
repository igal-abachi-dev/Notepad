using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Input.TextInput;
using Avalonia.Threading;
using Notepad.NeoEdit.Backend;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace Notepad.NeoEdit;

public sealed partial class NeoEditor : Control
{
    private enum EditKind { Independent, Typing, Overtype, Backspace, Delete }
    private const int MaxUndoEntries = 512;
    private const long MaxUndoCharacters = 4L * 1024 * 1024;

    private readonly record struct Selection(int Anchor, int Caret);
    private sealed record EditOp(int Offset, string Removed, string Inserted,
        Selection Before, Selection After, long BeforeRevision, long AfterRevision, EditKind Kind,
        string? BeforeNewLine = null, string? AfterNewLine = null)
    {
        public long StoredCharacters => (long)Removed.Length + Inserted.Length;
    }

    private PieceTreeEngine _doc = new("");
    private string? _snapshot = "";
    private int _caretOffset;
    private int _anchorOffset;
    private long _nextRevision;
    private long _savedRevision;
    private long _lastGroupEdit;
    private bool _continueGroup;
    private double? _preferredCaretX;
    private bool _caretVisible = true;
    private readonly DispatcherTimer _caretTimer;
    private readonly LinkedList<EditOp> _undo = new();
    private readonly Stack<EditOp> _redo = new();
    private long _undoCharacters;
    private (long Version, int Line, int[] Starts) _textElements = (-1, -1, []);

    public string Text { get => GetText(); set => LoadDocument(value); }
    public string NewLine { get; set; } = "\r\n";
    public bool PreservePastedLineEndings { get; set; }
    public bool WindowsOnlyLineEndings { get; set; }
    public int LineCount => _doc.LineCount;
    public int TextLength => _doc.Length;
    public long DocumentVersion { get; private set; }
    internal long SelectionVersion { get; private set; }
    public long Revision { get; private set; }
    public bool IsModified => Revision != _savedRevision;
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public static readonly DirectProperty<NeoEditor, bool> OvertypeModeProperty =
        AvaloniaProperty.RegisterDirect<NeoEditor, bool>(nameof(OvertypeMode), editor => editor.OvertypeMode);
    private bool _overtypeMode;
    public bool OvertypeMode => _overtypeMode;

    public int CaretOffset { get => _caretOffset; set => SetSelection(value, 0); }
    public int SelectionAnchor => _anchorOffset;

    public int SelectionStart
    {
        get => Math.Min(_caretOffset, _anchorOffset);
        set
        {
            int start = Math.Clamp(value, 0, _doc.Length);
            SetSelection(start, Math.Max(0, SelectionStart + SelectionLength - start));
        }
    }

    public int SelectionLength
    {
        get => Math.Abs(_caretOffset - _anchorOffset);
        set => SetSelection(SelectionStart, Math.Max(0, value));
    }

    public event EventHandler? TextChanged;
    public event EventHandler<(int Line, int Column)>? CaretMoved;
    public event EventHandler<int>? ZoomRequested;

    public NeoEditor()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Ibeam);
        _inputMethod = new EditorInputMethod(this);
        TextInputOptions.SetMultiline(this, true);
        TextInputMethodClientRequested += (_, e) => e.Client = _inputMethod;
        ScrollInvalidated += (_, _) => _inputMethod.NotifyGeometry();
        _caretTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) =>
        {
            _caretVisible = !_caretVisible;
            InvalidateVisual();
        });
        GotFocus += (_, _) =>
        {
            _caretVisible = true;
            _caretTimer.Start();
            InvalidateVisual();
        };
        LostFocus += (_, _) =>
        {
            _continueGroup = false;
            SetComposition(null);
            _caretTimer.Stop();
            InvalidateVisual();
        };
    }

    public string GetText() => _snapshot ??= _doc.GetTextInRange(0, _doc.Length);
    public string GetSelectedText() => _doc.GetTextInRange(SelectionStart, SelectionLength);

    public void LoadDocument(string? text, bool restoredDraft = false)
    {
        text ??= "";
        _inputMethod.Reset();
        _doc = new PieceTreeEngine(text);
        _snapshot = text;
        Revision = ++_nextRevision;
        _savedRevision = restoredDraft ? -1 : Revision;
        DocumentVersion++;
        _undo.Clear();
        _undoCharacters = 0;
        _redo.Clear();
        _continueGroup = false;
        _caretOffset = _anchorOffset = 0;
        _preferredCaretX = null;
        _wrappedRowEnd = false;
        if (OperatingSystem.IsWindows() && WindowsAutomationHandle != 0) _automationWindow?.ResetRanges();
        InvalidateDocumentLayout();
        Offset = default;
        NotifyCaretMoved();
        NotifyAutomationTextChanged();
    }

    public long CaptureSaveRevision()
    {
        _continueGroup = false;
        return Revision;
    }

    public void MarkSaved(long revision)
    {
        _savedRevision = revision;
        _continueGroup = false;
    }

    public void SetSelection(int start, int length)
    {
        int anchor = Math.Clamp(start, 0, _doc.Length);
        int caret = (int)Math.Clamp((long)start + length, 0, _doc.Length);
        if (anchor == caret)
            anchor = caret = SnapOffset(caret, 1);
        else
        {
            int direction = Math.Sign(caret - anchor);
            anchor = SnapOffset(anchor, -direction);
            caret = SnapOffset(caret, direction);
        }
        MoveSelection(anchor, caret);
    }

    public void Select(int start, int length) => SetSelection(start, length);
    public void SelectAll() => SetSelection(0, _doc.Length);

    private int SnapOffset(int offset, int direction)
    {
        if (offset > 0 && offset < _doc.Length)
        {
            string pair = _doc.GetTextInRange(offset - 1, 2);
            if (pair == "\r\n" || char.IsSurrogatePair(pair, 0))
                return offset + direction;
        }
        return offset;
    }

    private int AdjacentOffset(int offset, int direction) =>
        SnapOffset(Math.Clamp(offset + direction, 0, _doc.Length), direction);

    private int TextElementEdge(int offset, int direction, bool advance)
    {
        int first = Math.Max(0, offset - 2);
        var neighborhood = _doc.GetTextInRange(first, Math.Min(_doc.Length - first, offset - first + 2));
        bool ascii = true;
        foreach (char c in neighborhood)
            ascii &= c < 128;
        if (ascii) return advance ? AdjacentOffset(offset, direction) : offset;

        var (line, column) = _doc.GetLineColumnFromOffset(offset);
        int start = offset - column;
        int lineLength = _doc.GetLineContentLength(line);
        if (_textElements.Version != DocumentVersion || _textElements.Line != line)
            _textElements = (DocumentVersion, line, StringInfo.ParseCombiningCharacters(_doc.GetTextInRange(start, lineLength)));
        var starts = _textElements.Starts;
        int index = Array.BinarySearch(starts, column);
        bool boundary = index >= 0 || column == lineLength;
        if (boundary && !advance) return offset;
        if (index < 0)
            index = ~index - (direction < 0 ? 1 : 0);
        else
            index += direction;
        if (index >= 0 && index < starts.Length) return start + starts[index];
        if (direction > 0 && column < lineLength) return start + lineLength;
        return advance ? AdjacentOffset(offset, direction) : offset;
    }

    private void MoveSelection(int anchor, int caret, bool keepPreferredX = false, bool wrappedRowEnd = false)
    {
        _continueGroup = false;
        if (_anchorOffset != anchor || _caretOffset != caret) SelectionVersion++;
        _anchorOffset = anchor;
        _caretOffset = caret;
        _wrappedRowEnd = wrappedRowEnd;
        if (!keepPreferredX) _preferredCaretX = null;
        UpdateCaret();
    }

    public void MoveCaretToLine(int lineIndex) =>
        SetSelection(_doc.GetOffsetFromLineColumn(Math.Clamp(lineIndex, 0, LineCount - 1), 0), 0);

    public void ReplaceRange(int start, int length, string newText, int? endCaret = null) =>
        ReplaceRange(start, length, newText, EditKind.Independent, endCaret);

    internal void ConvertLineEndings(string ending)
    {
        string text = GetText();
        string converted = TextLineEndings.Normalize(text, ending);
        if (string.Equals(text, converted, StringComparison.Ordinal))
        {
            NewLine = ending;
            return;
        }
        var selection = new Selection(TextLineEndings.ConvertOffset(text, _anchorOffset, ending),
            TextLineEndings.ConvertOffset(text, _caretOffset, ending));
        ReplaceRange(0, text.Length, converted, EditKind.Independent, selection: selection, newLine: ending);
    }

    private void ReplaceRange(int start, int length, string text, EditKind kind, int? endCaret = null,
        Selection? selection = null, string? newLine = null)
    {
        if (start < 0 || start > _doc.Length) throw new ArgumentOutOfRangeException(nameof(start));
        if (length < 0 || length > _doc.Length - start) throw new ArgumentOutOfRangeException(nameof(length));
        // Explicit range edits may change individual line delimiters, but they may not split a Unicode scalar.
        if (SplitsSurrogate(start) || SplitsSurrogate(start + length) || !IsValidUtf16(text))
            throw new ArgumentException("The replacement would produce invalid Unicode.", nameof(text));

        string removed = _doc.GetTextInRange(start, length);
        if (removed == text)
        {
            _continueGroup = false;
            if (endCaret.HasValue) SetSelection(endCaret.Value, 0);
            return;
        }

        var before = new Selection(_anchorOffset, _caretOffset);
        long beforeRevision = Revision;
        string? beforeNewLine = newLine != null ? NewLine : null;
        ApplyChange(start, length, text);
        int caret = SnapOffset(Math.Clamp(endCaret ?? (start + text.Length), 0, _doc.Length), 1);
        var after = selection ?? new Selection(caret, caret);
        _anchorOffset = after.Anchor;
        _caretOffset = after.Caret;
        if (newLine != null) NewLine = newLine;
        Revision = ++_nextRevision;
        var edit = new EditOp(start, removed, text, before, after, beforeRevision, Revision, kind, beforeNewLine, newLine);
        var now = Environment.TickCount64;
        if (_continueGroup && now - _lastGroupEdit <= 1000 && _undo.Last?.Value is { } previous &&
            MergeEdits(previous, edit) is { } merged)
        {
            _undo.RemoveLast();
            _undoCharacters -= previous.StoredCharacters;
            edit = merged;
        }
        RememberUndo(edit);
        _redo.Clear();
        _continueGroup = kind != EditKind.Independent;
        _lastGroupEdit = now;
        PublishChange();
    }

    private void RememberUndo(EditOp edit)
    {
        _undo.AddLast(edit);
        _undoCharacters += edit.StoredCharacters;
        // Keep the most recent edit undoable even if a single large paste exceeds the usual budget.
        while (_undo.First != _undo.Last && (_undo.Count > MaxUndoEntries || _undoCharacters > MaxUndoCharacters))
        {
            _undoCharacters -= _undo.First!.Value.StoredCharacters;
            _undo.RemoveFirst();
        }
    }

    private bool SplitsSurrogate(int offset) =>
        offset > 0 && offset < _doc.Length && char.IsSurrogatePair(_doc.GetTextInRange(offset - 1, 2), 0);

    private static bool IsValidUtf16(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (!char.IsSurrogate(text[i])) continue;
            if (i + 1 >= text.Length || !char.IsSurrogatePair(text[i], text[i + 1])) return false;
            i++;
        }
        return true;
    }

    private static EditOp? MergeEdits(EditOp previous, EditOp next)
    {
        if (previous.Kind != next.Kind || previous.After != next.Before) return null;
        // Bound concatenation work for sustained typing and held keys.
        if ((long)previous.Inserted.Length + previous.Removed.Length + next.Inserted.Length + next.Removed.Length > 1024)
            return null;

        var merged = previous with { After = next.After, AfterRevision = next.AfterRevision };
        return next.Kind switch
        {
            EditKind.Typing when previous.Offset + previous.Inserted.Length == next.Offset =>
                merged with { Inserted = previous.Inserted + next.Inserted },
            EditKind.Overtype when previous.Offset + previous.Inserted.Length == next.Offset =>
                merged with { Removed = previous.Removed + next.Removed, Inserted = previous.Inserted + next.Inserted },
            EditKind.Backspace when next.Offset + next.Removed.Length == previous.Offset =>
                merged with { Offset = next.Offset, Removed = next.Removed + previous.Removed },
            EditKind.Delete when previous.Offset == next.Offset =>
                merged with { Removed = previous.Removed + next.Removed },
            _ => null
        };
    }

    private void ApplyChange(int offset, int length, string text)
    {
        int startLine = _doc.GetLineColumnFromOffset(Math.Max(0, offset - 1)).Item1;
        int oldLastLine = _doc.GetLineColumnFromOffset(Math.Min(_doc.Length, offset + length + (offset + length < _doc.Length ? 1 : 0))).Item1;
        int oldLineCount = _doc.LineCount;
        _doc.Delete(offset, length);
        _doc.Insert(offset, text);
        _snapshot = null;
        DocumentVersion++;
        if (OperatingSystem.IsWindows() && WindowsAutomationHandle != 0)
            _automationWindow?.TrackChange(offset, length, text.Length);
        InvalidateDocumentLayout(startLine, oldLastLine, oldLineCount);
    }

    private void PublishChange()
    {
        SetComposition(null);
        _preferredCaretX = null;
        _wrappedRowEnd = false;
        UpdateCaret();
        TextChanged?.Invoke(this, EventArgs.Empty);
        NotifyAutomationTextChanged();
    }

    private void UpdateCaret()
    {
        _caretVisible = true;
        EnsureCaretVisible();
        InvalidateVisual();
        NotifyCaretMoved();
    }

    private void NotifyCaretMoved()
    {
        var (line, column) = _doc.GetLineColumnFromOffset(_caretOffset);
        _inputMethod.NotifySelectionAndText();
        CaretMoved?.Invoke(this, (line + 1, column + 1));
        NotifyNativeAutomationSelection();
    }

    public void InsertAtCaret(string text) => ReplaceRange(SelectionStart, SelectionLength, text);

    public void DeleteSelection()
    {
        if (SelectionLength != 0) InsertAtCaret("");
    }

    public void Undo()
    {
        if (_undo.Last?.Value is not { } edit) return;
        _undo.RemoveLast();
        _undoCharacters -= edit.StoredCharacters;
        _redo.Push(edit);
        Replay(edit.Offset, edit.Inserted.Length, edit.Removed, edit.Before, edit.BeforeRevision, edit.BeforeNewLine);
    }

    public void Redo()
    {
        if (!_redo.TryPop(out var edit)) return;
        RememberUndo(edit);
        Replay(edit.Offset, edit.Removed.Length, edit.Inserted, edit.After, edit.AfterRevision, edit.AfterNewLine);
    }

    private void Replay(int offset, int length, string text, Selection selection, long revision, string? newLine)
    {
        _continueGroup = false;
        ApplyChange(offset, length, text);
        _anchorOffset = selection.Anchor;
        _caretOffset = selection.Caret;
        if (newLine != null && !WindowsOnlyLineEndings) NewLine = newLine;
        Revision = revision;
        PublishChange();
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);
        if (e.Handled || string.IsNullOrEmpty(e.Text) || !IsValidUtf16(e.Text)) return;
        bool composing = _preeditText.Length != 0;
        SetComposition(null);
        string text = TextLineEndings.Normalize(e.Text, NewLine);
        int start = SelectionStart;
        int length = SelectionLength;
        bool plainTyping = !composing && length == 0 && !text.AsSpan().ContainsAny('\r', '\n');
        if (plainTyping && OvertypeMode)
        {
            int end = start;
            int line = _doc.GetLineColumnFromOffset(start).Item1;
            int lineEnd = _doc.GetOffsetFromLineColumn(line, _doc.GetLineContentLength(line));
            for (int index = 0; index < text.Length && end < lineEnd;)
            {
                index += StringInfo.GetNextTextElementLength(text.AsSpan(index));
                end = TextElementEdge(end, 1, advance: true);
            }
            ReplaceRange(start, end - start, text, EditKind.Overtype, endCaret: start + text.Length);
        }
        else
            ReplaceRange(start, length, text, plainTyping ? EditKind.Typing : EditKind.Independent);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;
        if (_preeditText.Length > 0) return;

        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool alt = e.KeyModifiers.HasFlag(KeyModifiers.Alt);
        if (ctrl && alt) return; // AltGr combinations reach the platform text-input path.
        switch (e.Key)
        {
            case Key.Left when !alt: MoveHorizontal(-1, shift, ctrl); break;
            case Key.Right when !alt: MoveHorizontal(1, shift, ctrl); break;
            case Key.Up when !alt: MoveVertical(-1, shift); break;
            case Key.Down when !alt: MoveVertical(1, shift); break;
            case Key.Home when !alt: MoveToEdge(true, shift, ctrl); break;
            case Key.End when !alt: MoveToEdge(false, shift, ctrl); break;
            case Key.PageUp when !alt: MoveVertical(-PageRows, shift); break;
            case Key.PageDown when !alt: MoveVertical(PageRows, shift); break;
            case Key.Enter when !ctrl && !alt: InsertAtCaret(NewLine); break;
            case Key.Tab when !ctrl && !alt && !shift: InsertAtCaret("\t"); break;
            case Key.Insert when !ctrl && !alt && !shift:
                SetAndRaise(OvertypeModeProperty, ref _overtypeMode, !OvertypeMode);
                _continueGroup = false;
                _caretVisible = true;
                InvalidateVisual();
                break;
            case Key.Back when alt: Undo(); break;
            case Key.Back: DeleteAdjacent(-1, ctrl); break;
            case Key.Delete when !shift && !alt: DeleteAdjacent(1, ctrl); break;
            case Key.Z when ctrl && shift: Redo(); break;
            case Key.Z when ctrl: Undo(); break;
            case Key.Y when ctrl: Redo(); break;
            case Key.A when ctrl: SelectAll(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void DeleteAdjacent(int direction, bool word)
    {
        if (SelectionLength != 0)
        {
            DeleteSelection();
            return;
        }
        int start = TextElementEdge(_caretOffset, -direction, advance: false);
        int end = word ? WordOffset(start, direction) : TextElementEdge(start, direction, advance: true);
        ReplaceRange(Math.Min(start, end), Math.Abs(end - start), "",
            word ? EditKind.Independent : direction < 0 ? EditKind.Backspace : EditKind.Delete);
    }

    private void MoveHorizontal(int direction, bool select, bool word)
    {
        int target;
        if ((!select && SelectionLength > 0) || word)
            direction *= HorizontalReadingDirection();
        if (!select && !word && SelectionLength > 0)
            target = direction < 0 ? SelectionStart : SelectionStart + SelectionLength;
        else
            target = word ? WordOffset(_caretOffset, direction) : FindHorizontalTarget(direction);
        MoveSelection(select ? _anchorOffset : target, target);
    }

    private int WordOffset(int offset, int direction)
    {
        int Advance(int boundary) => TextElementEdge(boundary, direction, advance: true);

        int CategoryAt(int boundary)
        {
            int index = direction < 0 ? Advance(boundary) : boundary;
            return WordCategory(index);
        }

        offset = TextElementEdge(offset, -direction, advance: false);
        int edge = direction < 0 ? 0 : _doc.Length;
        if (direction < 0)
            while (offset != edge && CategoryAt(offset) == 0)
                offset = Advance(offset);
        if (offset == edge) return offset;
        int category = CategoryAt(offset);
        do { offset = Advance(offset); }
        while (offset != edge && CategoryAt(offset) == category);
        if (direction > 0 && category != 0)
            while (offset != edge && CategoryAt(offset) == 0)
                offset = Advance(offset);
        return offset;
    }

    private int WordCategory(int offset)
    {
        string value = _doc.GetTextInRange(offset, AdjacentOffset(offset, 1) - offset);
        if (value.Length == 0 || char.IsWhiteSpace(value, 0)) return 0;
        return char.GetUnicodeCategory(value, 0) is
            UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter or
            UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber or UnicodeCategory.OtherNumber or
            UnicodeCategory.ConnectorPunctuation or UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark ? 1 : 2;
    }

    public Task CutAsync() => CopySelectionAsync(removeSelection: true);
    public Task CopyAsync() => CopySelectionAsync(removeSelection: false);

    private (long Document, long Selection) CurrentSelection => (DocumentVersion, SelectionVersion);

    private async Task CopySelectionAsync(bool removeSelection)
    {
        var selection = CurrentSelection;
        var top = TopLevel.GetTopLevel(this);
        if (SelectionLength == 0 || top?.Clipboard is not { } clipboard) return;
        await clipboard.SetTextAsync(GetSelectedText());
        if (removeSelection && selection == CurrentSelection && TopLevel.GetTopLevel(this) == top)
            DeleteSelection();
    }

    public async Task PasteAsync()
    {
        var selection = CurrentSelection;
        var top = TopLevel.GetTopLevel(this);
        if (top?.Clipboard is not { } clipboard) return;
        string? text = await clipboard.TryGetTextAsync();
        if (!string.IsNullOrEmpty(text) && selection == CurrentSelection && TopLevel.GetTopLevel(this) == top)
        {
            if (WindowsOnlyLineEndings || !PreservePastedLineEndings)
                text = TextLineEndings.Normalize(text, WindowsOnlyLineEndings ? "\r\n" : NewLine);
            int start = SelectionStart;
            ReplaceRange(start, SelectionLength, text, endCaret: start + text.Length);
        }
    }
}
