using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Notepad.NeoEdit.Backend; // Ensure this matches your backend namespace

namespace Notepad.NeoEdit
{
    public sealed class NeoEditor : Control
    {
        // 1. Define Styled Properties (This allows XAML binding)
        public static readonly StyledProperty<string> TextProperty =
            AvaloniaProperty.Register<NeoEditor, string>(nameof(Text), "");

        public static readonly StyledProperty<FontFamily> FontFamilyProperty =
            AvaloniaProperty.Register<NeoEditor, FontFamily>(nameof(FontFamily), new FontFamily("Consolas"));

        public static readonly StyledProperty<double> FontSizeProperty =
            AvaloniaProperty.Register<NeoEditor, double>(nameof(FontSize), 14.0);

        public static readonly StyledProperty<IBrush> ForegroundProperty =
            AvaloniaProperty.Register<NeoEditor, IBrush>(nameof(Foreground), Brushes.Black);

        // 2. Public Properties wrappers
        public string Text
        {
            get => GetValue(TextProperty);
            set
            {
                SetValue(TextProperty, value);
                // When Text is set from ViewModel, reload the doc
                _doc = new PieceTreeEngine(value ?? "");
                _caretOffset = 0;
                _anchorOffset = 0;
                _layoutCache.Clear();
                InvalidateVisual();
            }
        }

        public FontFamily FontFamily
        {
            get => GetValue(FontFamilyProperty);
            set => SetValue(FontFamilyProperty, value);
        }

        public double FontSize
        {
            get => GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }

        public IBrush Foreground
        {
            get => GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        private PieceTreeEngine _doc;
        private readonly Typeface _typeface = new("Consolas");
        private readonly double _fontSize = 14;
        private readonly double _lineHeight = 22;
        private const int TabSize = 4;
        private const double GutterWidth = 50;

        // Visual Cache
        private readonly Dictionary<int, TextLayout> _layoutCache = new();
        private int _lastDocLength = -1;

        // State
        private int _caretOffset = 0;
        private int _anchorOffset = 0; // For selection
        private double _scrollOffsetY = 0;
        private double? _preferredCaretX = null;
        private bool _isDragging = false;

        // Blink
        private bool _caretVisible = true;
        private readonly DispatcherTimer _caretTimer;

        // Undo Stack
        private readonly Stack<EditOp> _undoStack = new();
        private readonly Stack<EditOp> _redoStack = new();

        private record EditOp(bool IsInsert, int Offset, string Text);

        public event EventHandler? TextChanged;
        public event EventHandler<(int Line, int Column)>? CaretMoved;
        public NeoEditor()
        {
            _doc = new PieceTreeEngine("");
            Focusable = true;
            Cursor = new Cursor(StandardCursorType.Ibeam);

            PointerPressed += OnPointerPressed;
            PointerMoved += OnPointerMoved;
            PointerReleased += (s, e) => _isDragging = false;
            PointerWheelChanged += OnScroll;

            _caretTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (s, e) =>
            {
                if (IsFocused)
                {
                    _caretVisible = !_caretVisible;
                    InvalidateVisual();
                }
            });

            GotFocus += (s, e) =>
            {
                _caretVisible = true;
                _caretTimer.Start();
                InvalidateVisual();
            };
            LostFocus += (s, e) =>
            {
                _caretVisible = false;
                _caretTimer.Stop();
                InvalidateVisual();
            };
        }
        private Typeface GetTypeface()
        {
            return new Typeface(FontFamily, FontStyle.Normal, FontWeight.Normal);
        }
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == TextProperty)
            {
                // Handled in setter above, or handle here if binding OneWay
                var newText = change.GetNewValue<string>();
                // Only reload if the change didn't come from our own internal typing
                // (Optimization: Check if _doc.Length matches to avoid reload loops)
                if (_doc.Length == 0 || newText != _doc.GetTextInRange(0, _doc.Length))
                {
                    _doc = new PieceTreeEngine(newText ?? "");
                    _layoutCache.Clear();
                    InvalidateVisual();
                }
            }
            else if (change.Property == FontFamilyProperty ||
                     change.Property == FontSizeProperty ||
                     change.Property == ForegroundProperty)
            {
                _layoutCache.Clear(); // Font changed, invalid layout
                InvalidateVisual();
            }
        }
        // --- Input ---

        protected override void OnTextInput(TextInputEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text)) return;
            string text = e.Text.Replace("\r\n", "\n").Replace("\r", "\n");

            DeleteSelection();
            ApplyInsert(_caretOffset, text, true);
            EnsureCaretVisible();
            InvalidateVisual();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
            bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

            // Navigation
            if (e.Key == Key.Left) MoveCaretHorizontal(-1, shift);
            else if (e.Key == Key.Right) MoveCaretHorizontal(1, shift);
            else if (e.Key == Key.Up) MoveCaretVertical(-1, shift);
            else if (e.Key == Key.Down) MoveCaretVertical(1, shift);
            else if (e.Key == Key.Home) MoveCaretToLineEdge(true, shift);
            else if (e.Key == Key.End) MoveCaretToLineEdge(false, shift);

            // Scroll Pages
            else if (e.Key == Key.PageUp) ScrollPage(-1, shift);
            else if (e.Key == Key.PageDown) ScrollPage(1, shift);

            // Editing
            else if (e.Key == Key.Enter)
            {
                DeleteSelection();
                ApplyInsert(_caretOffset, "\n", true);
            }
            else if (e.Key == Key.Back)
            {
                if (_caretOffset != _anchorOffset) DeleteSelection();
                else if (_caretOffset > 0) ApplyDelete(_caretOffset - 1, 1, true);
            }
            else if (e.Key == Key.Delete)
            {
                if (_caretOffset != _anchorOffset) DeleteSelection();
                else if (_caretOffset < _doc.Length) ApplyDelete(_caretOffset, 1, true);
            }

            // Commands
            else if (ctrl && e.Key == Key.Z) Undo();
            else if (ctrl && e.Key == Key.Y) Redo();
            else if (ctrl && e.Key == Key.C) CopyAsync();
            else if (ctrl && e.Key == Key.V) PasteAsync();
            else if (ctrl && e.Key == Key.X)
            {
                CopyAsync();
                DeleteSelection();
            }
            else if (ctrl && e.Key == Key.A)
            {
                _anchorOffset = 0;
                _caretOffset = _doc.Length;
                InvalidateVisual();
            }

            e.Handled = true;
        }

        // --- Logic ---

        private void ApplyInsert(int offset, string text, bool record)
        {
            _doc.Insert(offset, text);
            _caretOffset = offset + text.Length;
            _anchorOffset = _caretOffset;
            _preferredCaretX = null;
            if (record)
            {
                _undoStack.Push(new EditOp(true, offset, text));
                _redoStack.Clear();
            }
            TextChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ApplyDelete(int offset, int len, bool record)
        {
            // Scroll Anchor Logic: if deleting above viewport, adjust scroll
            var (line, _) = _doc.GetLineColumnFromOffset(offset);
            double topY = _scrollOffsetY;
            double delY = line * _lineHeight;

            string deletedText = record ? _doc.GetTextInRange(offset, len) : null;

            _doc.Delete(offset, len);
            _caretOffset = offset;
            _anchorOffset = offset;
            _preferredCaretX = null;

            if (delY < topY)
            {
                // Simple adjust: if we removed logic above
                // A full implementation would calculate exact line height removed
            }

            if (record)
            {
                _undoStack.Push(new EditOp(false, offset, deletedText));
                _redoStack.Clear();
            }
            TextChanged?.Invoke(this, EventArgs.Empty);
        }

        public void InsertAtCaret(string text)
        {

        }
        public void DeleteSelection()
        {
            if (_caretOffset == _anchorOffset) return;
            int start = Math.Min(_caretOffset, _anchorOffset);
            int len = Math.Abs(_caretOffset - _anchorOffset);
            ApplyDelete(start, len, true);
        }

        public void Undo()
        {
            if (_undoStack.Count == 0) return;
            var op = _undoStack.Pop();
            if (op.IsInsert) _doc.Delete(op.Offset, op.Text.Length);
            else _doc.Insert(op.Offset, op.Text);
            _caretOffset = op.IsInsert ? op.Offset : op.Offset + op.Text.Length;
            _anchorOffset = _caretOffset;
            _redoStack.Push(op);
            EnsureCaretVisible();
            InvalidateVisual();
        }

        public void Redo()
        {
            if (_redoStack.Count == 0) return;
            var op = _redoStack.Pop();
            if (op.IsInsert) _doc.Insert(op.Offset, op.Text);
            else _doc.Delete(op.Offset, op.Text.Length);
            _caretOffset = op.IsInsert ? op.Offset + op.Text.Length : op.Offset;
            _anchorOffset = _caretOffset;
            _undoStack.Push(op);
            EnsureCaretVisible();
            InvalidateVisual();
        }

        // --- Rendering ---

        public override void Render(DrawingContext context)
        {
            context.FillRectangle(Brushes.White, new Rect(Bounds.Size));
            context.FillRectangle(Brushes.WhiteSmoke, new Rect(0, 0, GutterWidth, Bounds.Height));

            if (_lastDocLength != _doc.Length)
            {
                _layoutCache.Clear();
                _lastDocLength = _doc.Length;
            }

            int firstLine = (int)(_scrollOffsetY / _lineHeight);
            int visibleLines = (int)(Bounds.Height / _lineHeight) + 2;
            int lastLine = Math.Min(_doc.LineCount, firstLine + visibleLines);

            double y = (firstLine * _lineHeight) - _scrollOffsetY;

            // Selection geometry
            int selStart = Math.Min(_caretOffset, _anchorOffset);
            int selEnd = Math.Max(_caretOffset, _anchorOffset);

            for (int i = firstLine; i < lastLine; i++)
            {
                // 1. Line Number
                var lineNum = new FormattedText((i + 1).ToString(), System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight, GetTypeface(), _fontSize * 0.8, Brushes.Gray);
                context.DrawText(lineNum, new Point(GutterWidth - lineNum.Width - 5, y + 2));

                // 2. Text Layout - Get Content & Expand Tabs
                string raw = _doc.GetLineContent(i) ?? "";
                string expanded = ExpandTabs(raw);

                // 3. Text Layout (Create or Retrieve from Cache)
                if (!_layoutCache.TryGetValue(i, out var layout))
                {
                    layout = new TextLayout(expanded, _typeface, _fontSize, Brushes.Black);
                    _layoutCache[i] = layout;
                }

                // 4. Selection Rendering
                int lineStartOffset = _doc.GetOffsetFromLineColumn(i, 0);
                int lineLen = raw.Length;
                int lineEndOffset = lineStartOffset + lineLen;

                if (selStart != selEnd)
                {
                    int s = Math.Max(selStart, lineStartOffset);
                    int e = Math.Min(selEnd, lineEndOffset);
                    if (s < e)
                    {
                        int vStart = DocToVisualIndex(raw, s - lineStartOffset);
                        int vEnd = DocToVisualIndex(raw, e - lineStartOffset);

                        foreach (var rect in layout.HitTestTextRange(vStart, vEnd - vStart))
                        {
                            context.FillRectangle(Brushes.LightBlue,
                                new Rect(rect.X + GutterWidth + 5, y, rect.Width, _lineHeight));
                        }
                    }
                }

                // 5. Syntax Highlighting Overlay (Regex) (Pass 'expanded' string explicitly)
                DrawSyntaxOverlay(context, layout, expanded, y);

                // 6. Draw Text
                using (context.PushTransform(Matrix.CreateTranslation(GutterWidth + 5, y)))
                {
                    layout.Draw(context, new Point(0, 0));
                }

                // 7. Caret Rendering
                if (_caretVisible && _caretOffset >= lineStartOffset && _caretOffset <= lineEndOffset)
                {
                    int localCaret = _caretOffset - lineStartOffset;
                    // Handle caret at end of line (valid position)
                    if (localCaret >= 0 && localCaret <= raw.Length)
                    {
                        int vCaret = DocToVisualIndex(raw, localCaret);
                        var hit = layout.HitTestTextPosition(vCaret);
                        context.DrawLine(new Pen(Brushes.Black, 1.5),
                            new Point(hit.X + GutterWidth + 5, y),
                            new Point(hit.X + GutterWidth + 5, y + _lineHeight));
                    }
                }

                y += _lineHeight;
            }
        }

        // --- Visual Helpers ---

        private static string ExpandTabs(string s)
        {
            if (s.IndexOf('\t') < 0) return s;
            var sb = new StringBuilder();
            int col = 0;
            foreach (char c in s)
            {
                if (c == '\t')
                {
                    int add = TabSize - (col % TabSize);
                    sb.Append(' ', add);
                    col += add;
                }
                else
                {
                    sb.Append(c);
                    col++;
                }
            }
            return sb.ToString();
        }

        private static int DocToVisualIndex(string s, int docIndex)
        {
            int v = 0;
            for (int i = 0; i < docIndex && i < s.Length; i++)
            {
                if (s[i] == '\t') v += TabSize - (v % TabSize);
                else v++;
            }
            return v;
        }

        private static int VisualToDocIndex(string s, int visualIndex)
        {
            int v = 0;
            for (int i = 0; i < s.Length; i++)
            {
                int w = (s[i] == '\t') ? TabSize - (v % TabSize) : 1;
                if (v + w > visualIndex) return i;
                v += w;
            }
            return s.Length;
        }

        private static readonly Regex RxKw =
            new(@"\b(using|namespace|public|private|class|void|int|string|return|new|var|if|else)\b",
                RegexOptions.Compiled);

        private static readonly Regex RxStr = new(@""".*?""", RegexOptions.Compiled);
        private static readonly Regex RxCom = new(@"//.*", RegexOptions.Compiled);

        // FIX: Added 'string text' argument because TextLayout doesn't expose it safely
        private void DrawSyntaxOverlay(DrawingContext ctx, TextLayout layout, string text, double y)
        {
            void Highlight(Regex rx, Color color)
            {
                foreach (Match m in rx.Matches(text))
                {
                    foreach (var rect in layout.HitTestTextRange(m.Index, m.Length))
                    {
                        ctx.FillRectangle(new SolidColorBrush(color, 0.25),
                            new Rect(rect.X + GutterWidth + 5, y, rect.Width, _lineHeight));
                    }
                }
            }

            Highlight(RxKw, Colors.Blue);
            Highlight(RxStr, Colors.Brown);
            Highlight(RxCom, Colors.Green);
        }

        // --- Navigation ---

        private void MoveCaretHorizontal(int delta, bool sel)
        {
            _caretOffset = Math.Clamp(_caretOffset + delta, 0, _doc.Length);
            if (!sel) _anchorOffset = _caretOffset;
            _preferredCaretX = null;
            EnsureCaretVisible();
            InvalidateVisual();

            var (line, col) = _doc.GetLineColumnFromOffset(_caretOffset);
            CaretMoved?.Invoke(this, (line + 1, col + 1));
        }

        private void MoveCaretVertical(int delta, bool sel)
        {
            var (line, _) = _doc.GetLineColumnFromOffset(_caretOffset);
            int nextLine = Math.Clamp(line + delta, 0, _doc.LineCount - 1);

            if (_preferredCaretX == null)
            {
                if (_layoutCache.TryGetValue(line, out var layout))
                {
                    int start = _doc.GetOffsetFromLineColumn(line, 0);
                    int local = _caretOffset - start;
                    string txt = _doc.GetLineContent(line) ?? "";
                    int vIndex = DocToVisualIndex(txt, local);

                    // FIX: Get X from the layout geometry directly
                    _preferredCaretX = layout.HitTestTextPosition(vIndex).X;
                }
            }

            string nextTxt = _doc.GetLineContent(nextLine) ?? "";
            string nextExp = ExpandTabs(nextTxt);
            var nextLayout = new TextLayout(nextExp, _typeface, _fontSize, Brushes.Black);

            // FIX: HitTestPoint now returns result, we use HitTestTextPosition to get X/Rect if needed, 
            // but here we just need the text index from the pixel X.
            var hit = nextLayout.HitTestPoint(new Point(_preferredCaretX ?? 0, 0));
            int vCol = hit.TextPosition;

            int docCol = VisualToDocIndex(nextTxt, vCol);

            _caretOffset = _doc.GetOffsetFromLineColumn(nextLine, docCol);
            if (!sel) _anchorOffset = _caretOffset;
            EnsureCaretVisible();
            InvalidateVisual();
            var (line2, col2) = _doc.GetLineColumnFromOffset(_caretOffset);
            CaretMoved?.Invoke(this, (line2 + 1, col2 + 1));
        }

        private void MoveCaretToLineEdge(bool start, bool sel)
        {
            var (line, _) = _doc.GetLineColumnFromOffset(_caretOffset);
            if (start) _caretOffset = _doc.GetOffsetFromLineColumn(line, 0);
            else
            {
                string txt = _doc.GetLineContent(line) ?? "";
                _caretOffset = _doc.GetOffsetFromLineColumn(line, txt.Length);
            }

            if (!sel) _anchorOffset = _caretOffset;
            _preferredCaretX = null;
            EnsureCaretVisible();
            InvalidateVisual();
            var (line2, col2) = _doc.GetLineColumnFromOffset(_caretOffset);
            CaretMoved?.Invoke(this, (line2 + 1, col2 + 1));
        }

        private void ScrollPage(int dir, bool sel)
        {
            int lines = (int)(Bounds.Height / _lineHeight);
            MoveCaretVertical(lines * dir, sel);
        }

        private void EnsureCaretVisible()
        {
            var (line, _) = _doc.GetLineColumnFromOffset(_caretOffset);
            double caretY = line * _lineHeight;

            if (caretY < _scrollOffsetY)
                _scrollOffsetY = caretY;
            else if (caretY + _lineHeight > _scrollOffsetY + Bounds.Height)
                _scrollOffsetY = caretY + _lineHeight - Bounds.Height;
        }

        private void OnPointerPressed(object sender, PointerPressedEventArgs e)
        {
            _isDragging = true;
            var p = e.GetPosition(this);
            p = p.WithX(p.X - GutterWidth - 5);

            double y = p.Y + _scrollOffsetY;
            int line = (int)(y / _lineHeight);
            line = Math.Clamp(line, 0, _doc.LineCount - 1);

            string txt = _doc.GetLineContent(line) ?? "";
            string exp = ExpandTabs(txt);
            var layout = new TextLayout(exp, _typeface, _fontSize, Brushes.Black);

            // FIX: Get TextPosition from HitTestResult
            var hit = layout.HitTestPoint(new Point(p.X, 0));
            int docCol = VisualToDocIndex(txt, hit.TextPosition);

            _caretOffset = _doc.GetOffsetFromLineColumn(line, docCol);

            if (!e.KeyModifiers.HasFlag(KeyModifiers.Shift)) _anchorOffset = _caretOffset;

            // FIX: Get correct X for preferred position
            _preferredCaretX = layout.HitTestTextPosition(hit.TextPosition).X;

            InvalidateVisual();
            Focus();

            var (line2, col2) = _doc.GetLineColumnFromOffset(_caretOffset);
            CaretMoved?.Invoke(this, (line2 + 1, col2 + 1));
        }

        private void OnPointerMoved(object sender, PointerEventArgs e)
        {
            if (_isDragging)
            {
                var p = e.GetPosition(this);
                p = p.WithX(p.X - GutterWidth - 5);

                double y = p.Y + _scrollOffsetY;
                int line = (int)(y / _lineHeight);
                line = Math.Clamp(line, 0, _doc.LineCount - 1);

                string txt = _doc.GetLineContent(line) ?? "";
                string exp = ExpandTabs(txt);
                var layout = new TextLayout(exp, _typeface, _fontSize, Brushes.Black);
                var hit = layout.HitTestPoint(new Point(p.X, 0));

                int docCol = VisualToDocIndex(txt, hit.TextPosition);
                _caretOffset = _doc.GetOffsetFromLineColumn(line, docCol);
                InvalidateVisual();
            }
        }

        private void OnScroll(object sender, PointerWheelEventArgs e)
        {
            _scrollOffsetY -= e.Delta.Y * _lineHeight * 3;
            _scrollOffsetY = Math.Max(0, _scrollOffsetY);
            double maxScroll = Math.Max(0, (_doc.LineCount * _lineHeight) - Bounds.Height);
            _scrollOffsetY = Math.Min(_scrollOffsetY, maxScroll);
            InvalidateVisual();
        }

        public void SelectAll() { _anchorOffset = 0; _caretOffset = _doc.Length; InvalidateVisual(); }
        public string GetText() => _doc.GetTextInRange(0, _doc.Length);

        public void ScrollToLine(int line)
        {
            double y = line * _lineHeight;
            _scrollOffsetY = Math.Max(0, y - (Bounds.Height / 2)); // Center it roughly
            InvalidateVisual();
        }
        public void Select(int start, int length)
        {
            _caretOffset = start + length;
            _anchorOffset = start;
            EnsureCaretVisible();
            InvalidateVisual();
        }
        public void Copy()
        {
            if (_caretOffset == _anchorOffset) return;
            int start = Math.Min(_caretOffset, _anchorOffset);
            int len = Math.Abs(_caretOffset - _anchorOffset);
            string txt = _doc.GetTextInRange(start, len);
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard != null)  top.Clipboard.SetTextAsync(txt).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public void Paste()
        {
            var top = TopLevel.GetTopLevel(this);
            if (top?.Clipboard != null)
            {
                string txt = top.Clipboard.GetTextAsync().ConfigureAwait(false).GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(txt))
                {
                    DeleteSelection();
                    ApplyInsert(_caretOffset, txt, true);
                    EnsureCaretVisible();
                    InvalidateVisual();
                }
            }
        }
    }
}