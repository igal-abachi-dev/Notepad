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

            var addBuffer = _buffers[0];
            int startOffset = addBuffer.Content.Length;
            addBuffer.Append(text);

            var lineStarts = new StringBuffer(text).LineStarts; // Helper usage to get stats
            int lfCnt = lineStarts.Count - 1;
            int endCol = text.Length - lineStarts[lfCnt];

            int startLine = GetLineIndex(addBuffer, startOffset);
            int startCol = startOffset - addBuffer.LineStarts[startLine];

            var newPiece = new Piece(0,
                new BufferCursor(startLine, startCol),
                new BufferCursor(GetLineIndex(addBuffer, startOffset + text.Length), endCol),
                lfCnt, text.Length
            );

            if (Root == Sentinel)
            {
                Root = new TreeNode(newPiece, NodeColor.Black);
            }
            else
            {
                var pos = NodeAt(offset);
                // Handle Append case where offset == Length
                if (offset == _cachedLength)
                {
                    // If tree is not empty, insert after right-most
                    if (Root != Sentinel) RBInsertRight(Righttest(Root), newPiece);
                    else Root = new TreeNode(newPiece, NodeColor.Black);
                }
                else if (pos.StartOffset == offset)
                {
                    RBInsertLeft(pos.Node, newPiece);
                }
                else if (pos.StartOffset + pos.Node.Piece.Length > offset)
                {
                    SplitNode(pos.Node, offset - pos.StartOffset, newPiece);
                }
                else
                {
                    RBInsertRight(pos.Node, newPiece);
                }
            }

            _cachedLength += text.Length;
            _cachedLineCount += lfCnt;
        }

        public void Delete(int offset, int cnt)
        {
            if (cnt <= 0) return;
            if (offset < 0 || offset + cnt > _cachedLength) throw new ArgumentOutOfRangeException(nameof(offset));

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

        private void SplitNode(TreeNode node, int offsetInNode, Piece newPiece)
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
        }

        private int ShrinkNode(TreeNode node, int startLocal, int endLocal)
        {
            Piece p = node.Piece;
            int originalLF = p.LineFeedCount;

            BufferCursor startCursor = GetBufferCursorFromOffset(p.BufferIndex, p.Start, startLocal);
            BufferCursor endCursor = GetBufferCursorFromOffset(p.BufferIndex, p.Start, endLocal);

            int leftLF = GetLineFeedCnt(p.BufferIndex, p.Start, startCursor);
            int rightLF = GetLineFeedCnt(p.BufferIndex, endCursor, p.End);
            int deletedLF = GetLineFeedCnt(p.BufferIndex, startCursor, endCursor);

            node.Piece = new Piece(p.BufferIndex, p.Start, startCursor, leftLF, startLocal);
            UpdateTreeMetadata(node, node.Piece.Length - p.Length, node.Piece.LineFeedCount - originalLF);

            if (p.Length - endLocal > 0)
            {
                var rightPiece = new Piece(p.BufferIndex, endCursor, p.End, rightLF, p.Length - endLocal);
                RBInsertRight(node, rightPiece);
            }
            return -deletedLF;
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
            Piece removed = z.Piece;
            TreeNode y = (z.Left == Sentinel || z.Right == Sentinel) ? z : Leftest(z.Right);
            TreeNode x = (y.Left != Sentinel) ? y.Left : y.Right;

            bool xIsSentinel = (x == Sentinel);
            if (xIsSentinel) Sentinel.Parent = y.Parent;

            if (y == z)
            {
                UpdateTreeMetadata(y, -removed.Length, -removed.LineFeedCount);
            }
            else
            {
                UpdateTreeMetadataUntil(y, z, -y.Piece.Length, -y.Piece.LineFeedCount);
                UpdateTreeMetadata(z, -removed.Length, -removed.LineFeedCount);
                z.Piece = y.Piece;
            }

            if (y.Parent == Sentinel) Root = x;
            else if (y == y.Parent.Left) y.Parent.Left = x;
            else y.Parent.Right = x;

            if (!xIsSentinel) x.Parent = y.Parent;

            if (y.Color == NodeColor.Black) RBDeleteFixup(x);
            if (xIsSentinel) Sentinel.Parent = Sentinel;
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

        private void UpdateTreeMetadataUntil(TreeNode x, TreeNode end, int sizeDelta, int lfDelta)
        {
            while (x != end && x != Sentinel)
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

        private void RBDeleteFixup(TreeNode x)
        {
            while (x != Root && x.Color == NodeColor.Black)
            {
                if (x == x.Parent.Left)
                {
                    TreeNode w = x.Parent.Right;
                    if (w.Color == NodeColor.Red)
                    {
                        w.Color = NodeColor.Black;
                        x.Parent.Color = NodeColor.Red;
                        LeftRotate(x.Parent);
                        w = x.Parent.Right;
                    }
                    if (w.Left.Color == NodeColor.Black && w.Right.Color == NodeColor.Black)
                    {
                        w.Color = NodeColor.Red;
                        x = x.Parent;
                    }
                    else
                    {
                        if (w.Right.Color == NodeColor.Black)
                        {
                            w.Left.Color = NodeColor.Black;
                            w.Color = NodeColor.Red;
                            RightRotate(w);
                            w = x.Parent.Right;
                        }
                        w.Color = x.Parent.Color;
                        x.Parent.Color = NodeColor.Black;
                        w.Right.Color = NodeColor.Black;
                        LeftRotate(x.Parent);
                        x = Root;
                    }
                }
                else
                {
                    TreeNode w = x.Parent.Left;
                    if (w.Color == NodeColor.Red)
                    {
                        w.Color = NodeColor.Black;
                        x.Parent.Color = NodeColor.Red;
                        RightRotate(x.Parent);
                        w = x.Parent.Left;
                    }
                    if (w.Right.Color == NodeColor.Black && w.Left.Color == NodeColor.Black)
                    {
                        w.Color = NodeColor.Red;
                        x = x.Parent;
                    }
                    else
                    {
                        if (w.Left.Color == NodeColor.Black)
                        {
                            w.Right.Color = NodeColor.Black;
                            w.Color = NodeColor.Red;
                            LeftRotate(w);
                            w = x.Parent.Left;
                        }
                        w.Color = x.Parent.Color;
                        x.Parent.Color = NodeColor.Black;
                        w.Left.Color = NodeColor.Black;
                        RightRotate(x.Parent);
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
            if (start.Line == end.Line)
            {
                var buffer = _buffers[bufferIndex];
                int endAbs = buffer.LineStarts[end.Line] + end.Column;
                // If we split right between \r and \n, we must count it as a LF to preserve structure
                if (endAbs > 0 && endAbs < buffer.Content.Length &&
                    buffer.Content[endAbs] == '\n' && buffer.Content[endAbs - 1] == '\r')
                    return 1;
                return 0;
            }
            return end.Line - start.Line;
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
                    return offset + (absOffset - pieceStartAbs);
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