using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Notepad.NeoEdit.Backend
{

    // --- The Piece Table Engine text buffer, like vs code/ vs2026 ---
    public class PieceTreeEngine
    {
        public static readonly TreeNode Sentinel;

        static PieceTreeEngine()
        {
            Sentinel = new TreeNode(null, NodeColor.Black);
            Sentinel.Parent = Sentinel;
            Sentinel.Left = Sentinel;
            Sentinel.Right = Sentinel;
        }

        public TreeNode Root;
        private readonly List<StringBuffer> _buffers;
        private int _cachedLength;
        private int _cachedLineCount;

        public PieceTreeEngine(string initialContent="")
        {
            Root = Sentinel;
            _buffers = new List<StringBuffer> { new StringBuffer("") }; // Buffer 0: Add Buffer

            if (!string.IsNullOrEmpty(initialContent))
            {
                _buffers.Add(new StringBuffer(initialContent));
                var buf = _buffers[1];
                int lfCnt = buf.LineStarts.Count - 1;
                var piece = new Piece(1,
                    new BufferCursor(0, 0),
                    new BufferCursor(lfCnt, initialContent.Length - buf.LineStarts[lfCnt]),
                    lfCnt, initialContent.Length);

                Root = new TreeNode(piece, NodeColor.Black);
                _cachedLength = initialContent.Length;
                _cachedLineCount = lfCnt + 1;
            }
            else
            {
                _cachedLength = 0;
                _cachedLineCount = 1;
            }
        }

        public int Length => _cachedLength;
        public int LineCount => _cachedLineCount;

        // --- Core Operations ---

        public void Insert(int offset, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            if (offset < 0 || offset > _cachedLength) throw new ArgumentOutOfRangeException(nameof(offset));

            InsertCore(offset, text);
            if (text[0] == '\n') JoinCrLfAt(offset);
            if (text[^1] == '\r') JoinCrLfAt(offset + text.Length);
        }

        private void InsertCore(int offset, string text)
        {
            var addBuffer = _buffers[0];
            int startOffset = addBuffer.Content.Length;
            int startLine = addBuffer.LineStarts.Count - 1;
            var start = new BufferCursor(startLine, startOffset - addBuffer.LineStarts[startLine]);
            bool splitCrLf = text[0] == '\n' && startOffset > 0 && addBuffer.Content[startOffset - 1] == '\r';

            addBuffer.Append(text);
            int endLine = addBuffer.LineStarts.Count - 1;
            var end = new BufferCursor(endLine, addBuffer.Content.Length - addBuffer.LineStarts[endLine]);
            int lfCnt = endLine - startLine;

            if (offset == 0)
            {
                var newPiece = new Piece(0, start, end, lfCnt, text.Length);
                if (Root == Sentinel)
                    Root = new TreeNode(newPiece, NodeColor.Black);
                else
                    RBInsertLeft(Leftest(Root), newPiece);
            }
            else
            {
                var previous = NodeAt(offset - 1);
                var node = previous.Node;
                var piece = node.Piece;
                bool atPieceEnd = previous.Remainder + 1 == piece.Length;
                if (atPieceEnd && !splitCrLf && piece.BufferIndex == 0 &&
                    piece.End.Line == start.Line && piece.End.Column == start.Column)
                {
                    // Adjacent edits can share a piece when their text is also contiguous in the add buffer.
                    node.Piece = new Piece(0, piece.Start, end, piece.LineFeedCount + lfCnt,
                        piece.Length + text.Length);
                    UpdateTreeMetadata(node, text.Length, lfCnt);
                }
                else
                {
                    var newPiece = new Piece(0, start, end, lfCnt, text.Length);
                    if (atPieceEnd)
                        RBInsertRight(node, newPiece);
                    else
                        lfCnt += SplitNode(node, previous.Remainder + 1, newPiece);
                }
            }

            _cachedLength += text.Length;
            _cachedLineCount += lfCnt;
        }

        public void Delete(int offset, int cnt)
        {
            if (cnt <= 0) return;
            if (offset < 0 || offset > _cachedLength - cnt)
                throw new ArgumentOutOfRangeException(nameof(offset));

            DeleteCore(offset, cnt);
            JoinCrLfAt(offset);
        }

        private void DeleteCore(int offset, int cnt)
        {
            var startPos = NodeAt(offset);
            var endPos = NodeAt(offset + cnt);

            int lfDelta = 0;

            if (startPos.Node == endPos.Node)
            {
                lfDelta = ShrinkNode(startPos.Node, startPos.Remainder, endPos.Remainder);
                if (startPos.Node.Piece.Length == 0) RBDelete(startPos.Node);
            }
            else
            {
                var startNode = startPos.Node;
                var endNode = endPos.Node;

                lfDelta += DeleteNodeTail(startNode, startPos.Remainder);
                lfDelta += DeleteNodeHead(endNode, endPos.Remainder);

                TreeNode node = startNode.Next();
                while (node != Sentinel && node != endNode)
                {
                    TreeNode next = node.Next();
                    lfDelta -= node.Piece.LineFeedCount;
                    RBDelete(node);
                    node = next;
                }

                if (startNode.Piece.Length == 0) RBDelete(startNode);
                if (endNode.Piece.Length == 0) RBDelete(endNode);
            }

            _cachedLength -= cnt;
            _cachedLineCount += lfDelta;
        }

        private void JoinCrLfAt(int offset)
        {
            if (offset <= 0 || offset >= _cachedLength) return;
            if (NodeAt(offset - 1).Node == NodeAt(offset).Node) return;
            if (GetTextInRange(offset - 1, 2) != "\r\n") return;

            // Keep CRLF in one piece so subtree line counts remain additive.
            DeleteCore(offset - 1, 2);
            InsertCore(offset - 1, "\r\n");
        }

        // --- Lookups ---

        public string GetLineContent(int lineNumber)
        {
            if (lineNumber < 0 || lineNumber >= _cachedLineCount) return null;
            int offset = GetOffsetAtLine(lineNumber);
            int nextLineOffset = (lineNumber == _cachedLineCount - 1) ? _cachedLength : GetOffsetAtLine(lineNumber + 1);
            int len = nextLineOffset - offset;
            string raw = GetTextInRange(offset, len);

            // Trim EOL
            if (raw.EndsWith("\r\n")) return raw.Substring(0, raw.Length - 2);
            if (raw.EndsWith("\n") || raw.EndsWith("\r")) return raw.Substring(0, raw.Length - 1);
            return raw;
        }

        public (int line, int col) GetLineColumnFromOffset(int offset)
        {
            offset = Math.Clamp(offset, 0, _cachedLength);

            int low = 0;
            int high = _cachedLineCount - 1;

            // Binary search line starts
            while (low <= high)
            {
                int mid = (low + high) / 2;
                int lineStart = GetOffsetAtLine(mid);
                int nextStart = (mid == _cachedLineCount - 1) ? _cachedLength : GetOffsetAtLine(mid + 1);

                if (offset >= lineStart && offset < nextStart)
                    return (mid, offset - lineStart);

                if (offset < lineStart) high = mid - 1;
                else low = mid + 1;
            }

            int lastStart = GetOffsetAtLine(_cachedLineCount - 1);
            return (_cachedLineCount - 1, offset - lastStart);
        }

        public int GetOffsetFromLineColumn(int line, int col)
        {
            if (_cachedLength == 0) return 0;
            line = Math.Clamp(line, 0, _cachedLineCount - 1);
            int lineStart = GetOffsetAtLine(line);

            int nextStart = (line == _cachedLineCount - 1) ? _cachedLength : GetOffsetAtLine(line + 1);
            int maxCol = nextStart - lineStart;

            // If the line ends with CRLF/LF, we typically want to clamp before the newline char
            // For editing, it's safer to allow going to the newline char to append
            return lineStart + Math.Clamp(col, 0, maxCol);
        }

        public string GetTextInRange(int offset, int length)
        {
            if (length == 0) return "";
            var sb = new StringBuilder(length);
            var pos = NodeAt(offset);
            TreeNode node = pos.Node;

            // Edge case: if NodeAt returns Sentinel/EOF logic, we might need adjustments
            if (node == Sentinel) return "";

            int internalOffset = pos.Remainder;
            int remaining = length;

            while (remaining > 0 && node != Sentinel)
            {
                var buf = _buffers[node.Piece.BufferIndex];
                int pieceStartAbs = buf.LineStarts[node.Piece.Start.Line] + node.Piece.Start.Column;
                int readStart = pieceStartAbs + internalOffset;

                int charCountInPiece = node.Piece.Length - internalOffset;
                int canRead = Math.Min(charCountInPiece, remaining);

                sb.Append(buf.Content.ToString(readStart, canRead));

                remaining -= canRead;
                internalOffset = 0;
                node = node.Next();
            }
            return sb.ToString();
        }

        // --- Internal Algorithms ---

        private NodePosition NodeAt(int offset)
        {
            if (Root == Sentinel) return new NodePosition { Node = Sentinel, StartOffset = 0, Remainder = 0 };

            if (offset == _cachedLength)
            {
                var last = Righttest(Root);
                return new NodePosition { Node = last, StartOffset = _cachedLength - last.Piece.Length, Remainder = last.Piece.Length };
            }

            TreeNode x = Root;
            int nodeStartOffset = 0;
            while (x != Sentinel)
            {
                if (x.SizeLeft > offset) x = x.Left;
                else if (x.SizeLeft + x.Piece.Length > offset)
                {
                    nodeStartOffset += x.SizeLeft;
                    return new NodePosition { Node = x, StartOffset = nodeStartOffset, Remainder = offset - x.SizeLeft };
                }
                else
                {
                    offset -= (x.SizeLeft + x.Piece.Length);
                    nodeStartOffset += (x.SizeLeft + x.Piece.Length);
                    x = x.Right;
                }
            }
            return new NodePosition { Node = Sentinel, StartOffset = 0, Remainder = 0 };
        }

        private int SplitNode(TreeNode node, int offsetInNode, Piece newPiece)
        {
            Piece original = node.Piece;
            BufferCursor splitPos = GetBufferCursorFromOffset(original.BufferIndex, original.Start, offsetInNode);

            int originalLF = original.LineFeedCount;
            int leftLF = GetLineFeedCnt(original.BufferIndex, original.Start, splitPos);
            int rightLF = GetLineFeedCnt(original.BufferIndex, splitPos, original.End);

            node.Piece = new Piece(original.BufferIndex, original.Start, splitPos, leftLF, offsetInNode);
            var rightPiece = new Piece(original.BufferIndex, splitPos, original.End, rightLF, original.Length - offsetInNode);

            UpdateTreeMetadata(node, node.Piece.Length - original.Length, node.Piece.LineFeedCount - originalLF);

            TreeNode newNode = RBInsertRight(node, newPiece);
            if (rightPiece.Length > 0) RBInsertRight(newNode, rightPiece);
            return leftLF + rightLF - originalLF;
        }

        private int ShrinkNode(TreeNode node, int startLocal, int endLocal)
        {
            Piece p = node.Piece;
            int originalLF = p.LineFeedCount;

            BufferCursor startCursor = GetBufferCursorFromOffset(p.BufferIndex, p.Start, startLocal);
            BufferCursor endCursor = GetBufferCursorFromOffset(p.BufferIndex, p.Start, endLocal);

            int leftLF = GetLineFeedCnt(p.BufferIndex, p.Start, startCursor);
            int rightLF = GetLineFeedCnt(p.BufferIndex, endCursor, p.End);

            node.Piece = new Piece(p.BufferIndex, p.Start, startCursor, leftLF, startLocal);
            UpdateTreeMetadata(node, node.Piece.Length - p.Length, node.Piece.LineFeedCount - originalLF);

            if (p.Length - endLocal > 0)
            {
                var rightPiece = new Piece(p.BufferIndex, endCursor, p.End, rightLF, p.Length - endLocal);
                RBInsertRight(node, rightPiece);
            }
            return leftLF + rightLF - originalLF;
        }

        private int DeleteNodeTail(TreeNode node, int posLocal)
        {
            Piece p = node.Piece;
            BufferCursor newEnd = GetBufferCursorFromOffset(p.BufferIndex, p.Start, posLocal);
            int newLF = GetLineFeedCnt(p.BufferIndex, p.Start, newEnd);

            int sizeDelta = posLocal - p.Length;
            int lfDelta = newLF - p.LineFeedCount;

            node.Piece = new Piece(p.BufferIndex, p.Start, newEnd, newLF, posLocal);
            UpdateTreeMetadata(node, sizeDelta, lfDelta);
            return lfDelta;
        }

        private int DeleteNodeHead(TreeNode node, int posLocal)
        {
            Piece p = node.Piece;
            BufferCursor newStart = GetBufferCursorFromOffset(p.BufferIndex, p.Start, posLocal);
            int newLF = GetLineFeedCnt(p.BufferIndex, newStart, p.End);
            int newLen = p.Length - posLocal;

            int sizeDelta = newLen - p.Length;
            int lfDelta = newLF - p.LineFeedCount;

            node.Piece = new Piece(p.BufferIndex, newStart, p.End, newLF, newLen);
            UpdateTreeMetadata(node, sizeDelta, lfDelta);
            return lfDelta;
        }

        // --- Tree Balancing ---

        private TreeNode RBInsertRight(TreeNode node, Piece p)
        {
            TreeNode z = new TreeNode(p, NodeColor.Red);
            if (node.Right == Sentinel) { node.Right = z; z.Parent = node; }
            else
            {
                TreeNode next = Leftest(node.Right);
                next.Left = z;
                z.Parent = next;
            }
            UpdateTreeMetadata(z, z.Piece.Length, z.Piece.LineFeedCount);
            FixInsert(z);
            return z;
        }

        private TreeNode RBInsertLeft(TreeNode node, Piece p)
        {
            TreeNode z = new TreeNode(p, NodeColor.Red);
            if (node.Left == Sentinel) { node.Left = z; z.Parent = node; }
            else
            {
                TreeNode prev = Righttest(node.Left);
                prev.Right = z;
                z.Parent = prev;
            }
            UpdateTreeMetadata(z, z.Piece.Length, z.Piece.LineFeedCount);
            FixInsert(z);
            return z;
        }

        private void RBDelete(TreeNode z)
        {
            TreeNode successor = z.Left == Sentinel || z.Right == Sentinel ? z : Leftest(z.Right);
            TreeNode child = successor.Left != Sentinel ? successor.Left : successor.Right;
            var removedColor = successor.Color;
            var childParent = successor.Parent;

            UpdateTreeMetadata(successor, -successor.Piece.Length, -successor.Piece.LineFeedCount);
            ReplaceNode(successor, child);

            if (successor != z)
            {
                // Move the successor itself. Range deletion may still hold a reference to it.
                UpdateTreeMetadata(z, successor.Piece.Length - z.Piece.Length,
                    successor.Piece.LineFeedCount - z.Piece.LineFeedCount);
                ReplaceNode(z, successor);
                successor.Left = z.Left;
                successor.Left.Parent = successor;
                successor.Right = z.Right;
                if (successor.Right != Sentinel) successor.Right.Parent = successor;
                successor.SizeLeft = z.SizeLeft;
                successor.LFLeft = z.LFLeft;
                successor.Color = z.Color;
                if (childParent == z) childParent = successor;
            }

            if (removedColor == NodeColor.Black) RBDeleteFixup(child, childParent);
        }

        private void ReplaceNode(TreeNode node, TreeNode replacement)
        {
            if (node.Parent == Sentinel) Root = replacement;
            else if (node.Parent.Left == node) node.Parent.Left = replacement;
            else node.Parent.Right = replacement;
            if (replacement != Sentinel) replacement.Parent = node.Parent;
        }

        private void UpdateTreeMetadata(TreeNode x, int sizeDelta, int lfDelta)
        {
            while (x != Root && x != Sentinel)
            {
                if (x.Parent.Left == x)
                {
                    x.Parent.SizeLeft += sizeDelta;
                    x.Parent.LFLeft += lfDelta;
                }
                x = x.Parent;
            }
        }

        // --- Rotations & Fixups ---
        private void FixInsert(TreeNode k)
        {
            while (k != Root && k.Parent.Color == NodeColor.Red)
            {
                if (k.Parent == k.Parent.Parent.Left)
                {
                    TreeNode u = k.Parent.Parent.Right;
                    if (u.Color == NodeColor.Red)
                    {
                        k.Parent.Color = NodeColor.Black;
                        u.Color = NodeColor.Black;
                        k.Parent.Parent.Color = NodeColor.Red;
                        k = k.Parent.Parent;
                    }
                    else
                    {
                        if (k == k.Parent.Right) { k = k.Parent; LeftRotate(k); }
                        k.Parent.Color = NodeColor.Black;
                        k.Parent.Parent.Color = NodeColor.Red;
                        RightRotate(k.Parent.Parent);
                    }
                }
                else
                {
                    TreeNode u = k.Parent.Parent.Left;
                    if (u.Color == NodeColor.Red)
                    {
                        k.Parent.Color = NodeColor.Black;
                        u.Color = NodeColor.Black;
                        k.Parent.Parent.Color = NodeColor.Red;
                        k = k.Parent.Parent;
                    }
                    else
                    {
                        if (k == k.Parent.Left) { k = k.Parent; RightRotate(k); }
                        k.Parent.Color = NodeColor.Black;
                        k.Parent.Parent.Color = NodeColor.Red;
                        LeftRotate(k.Parent.Parent);
                    }
                }
            }
            Root.Color = NodeColor.Black;
        }

        private void RBDeleteFixup(TreeNode x, TreeNode parent)
        {
            while (x != Root && x.Color == NodeColor.Black)
            {
                if (x == parent.Left)
                {
                    TreeNode w = parent.Right;
                    if (w.Color == NodeColor.Red)
                    {
                        w.Color = NodeColor.Black;
                        parent.Color = NodeColor.Red;
                        LeftRotate(parent);
                        w = parent.Right;
                    }
                    if (w.Left.Color == NodeColor.Black && w.Right.Color == NodeColor.Black)
                    {
                        w.Color = NodeColor.Red;
                        x = parent;
                        parent = x.Parent;
                    }
                    else
                    {
                        if (w.Right.Color == NodeColor.Black)
                        {
                            w.Left.Color = NodeColor.Black;
                            w.Color = NodeColor.Red;
                            RightRotate(w);
                            w = parent.Right;
                        }
                        w.Color = parent.Color;
                        parent.Color = NodeColor.Black;
                        w.Right.Color = NodeColor.Black;
                        LeftRotate(parent);
                        x = Root;
                    }
                }
                else
                {
                    TreeNode w = parent.Left;
                    if (w.Color == NodeColor.Red)
                    {
                        w.Color = NodeColor.Black;
                        parent.Color = NodeColor.Red;
                        RightRotate(parent);
                        w = parent.Left;
                    }
                    if (w.Right.Color == NodeColor.Black && w.Left.Color == NodeColor.Black)
                    {
                        w.Color = NodeColor.Red;
                        x = parent;
                        parent = x.Parent;
                    }
                    else
                    {
                        if (w.Left.Color == NodeColor.Black)
                        {
                            w.Right.Color = NodeColor.Black;
                            w.Color = NodeColor.Red;
                            LeftRotate(w);
                            w = parent.Left;
                        }
                        w.Color = parent.Color;
                        parent.Color = NodeColor.Black;
                        w.Left.Color = NodeColor.Black;
                        RightRotate(parent);
                        x = Root;
                    }
                }
            }
            x.Color = NodeColor.Black;
        }

        private void LeftRotate(TreeNode x)
        {
            TreeNode y = x.Right;
            y.SizeLeft += x.SizeLeft + x.Piece.Length;
            y.LFLeft += x.LFLeft + x.Piece.LineFeedCount;
            x.Right = y.Left;
            if (y.Left != Sentinel) y.Left.Parent = x;
            y.Parent = x.Parent;
            if (x.Parent == Sentinel) Root = y;
            else if (x.Parent.Left == x) x.Parent.Left = y;
            else x.Parent.Right = y;
            y.Left = x;
            x.Parent = y;
        }

        private void RightRotate(TreeNode y)
        {
            TreeNode x = y.Left;
            y.SizeLeft -= (x.SizeLeft + x.Piece.Length);
            y.LFLeft -= (x.LFLeft + x.Piece.LineFeedCount);
            y.Left = x.Right;
            if (x.Right != Sentinel) x.Right.Parent = y;
            x.Parent = y.Parent;
            if (y.Parent == Sentinel) Root = x;
            else if (y.Parent.Right == y) y.Parent.Right = x;
            else y.Parent.Left = x;
            x.Right = y;
            y.Parent = x;
        }

        // --- Helpers ---

        private int GetLineIndex(StringBuffer buf, int offset)
        {
            int idx = buf.LineStarts.BinarySearch(offset);
            return idx < 0 ? (~idx - 1) : idx;
        }

        private BufferCursor GetBufferCursorFromOffset(int bufferIndex, BufferCursor start, int offsetLocal)
        {
            var buf = _buffers[bufferIndex];
            int absStart = buf.LineStarts[start.Line] + start.Column;
            int target = absStart + offsetLocal;
            int line = GetLineIndex(buf, target);
            return new BufferCursor(line, target - buf.LineStarts[line]);
        }

        private int GetLineFeedCnt(int bufferIndex, BufferCursor start, BufferCursor end)
        {
            int count = end.Line - start.Line;
            if (end.Column == 0 || (count == 0 && start.Column == end.Column)) return count;

            var buffer = _buffers[bufferIndex];
            int endAbs = buffer.LineStarts[end.Line] + end.Column;
            // A piece ending between CR and LF contains a standalone CR.
            if (endAbs < buffer.Content.Length &&
                buffer.Content[endAbs - 1] == '\r' && buffer.Content[endAbs] == '\n')
                count++;
            return count;
        }

        private int GetOffsetAtLine(int lineNumber)
        {
            if (lineNumber <= 0) return 0;
            int targetLF = lineNumber - 1;

            TreeNode x = Root;
            int offset = 0;

            while (x != Sentinel)
            {
                if (x.LFLeft > targetLF) x = x.Left;
                else if (x.LFLeft + x.Piece.LineFeedCount > targetLF)
                {
                    offset += x.SizeLeft;
                    int relativeLF = targetLF - x.LFLeft;
                    var buf = _buffers[x.Piece.BufferIndex];
                    int startLine = x.Piece.Start.Line;
                    int targetLineIdx = startLine + relativeLF + 1;
                    int absOffset = buf.LineStarts[targetLineIdx];
                    int pieceStartAbs = buf.LineStarts[startLine] + x.Piece.Start.Column;
                    return offset + Math.Min(absOffset - pieceStartAbs, x.Piece.Length);
                }
                else
                {
                    targetLF -= (x.LFLeft + x.Piece.LineFeedCount);
                    offset += x.SizeLeft + x.Piece.Length;
                    x = x.Right;
                }
            }
            return _cachedLength;
        }

        private static TreeNode Leftest(TreeNode node) { while (node.Left != Sentinel) node = node.Left; return node; }
        private static TreeNode Righttest(TreeNode node) { while (node.Right != Sentinel) node = node.Right; return node; }
    }
}
