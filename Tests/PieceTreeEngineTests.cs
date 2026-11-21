using System;
using System.Text.RegularExpressions;
using Notepad.NeoEdit.Backend;
using Xunit;

namespace Notepad.Tests;

public class PieceTreeEngineTests
{
    [Fact]
    public void Delete_AcrossPieces_PreservesTheSuccessor()
    {
        var tree = new PieceTreeEngine();
        foreach (char character in "dcba")
            tree.Insert(0, character.ToString());

        tree.Delete(0, 3);

        Assert.Equal("d", tree.GetTextInRange(0, tree.Length));
        AssertTree(tree);
    }

    [Fact]
    public void Insert_AdjacentTyping_ReusesOnePieceAndTracksLineBreaks()
    {
        var tree = new PieceTreeEngine();
        tree.Insert(0, "one");
        tree.Insert(tree.Length, "\n");
        tree.Insert(tree.Length, "two");

        Assert.Same(PieceTreeEngine.Sentinel, tree.Root.Left);
        Assert.Same(PieceTreeEngine.Sentinel, tree.Root.Right);
        AssertMatchesText(tree, "one\ntwo");
    }

    [Fact]
    public void Insert_AdjacentTypingInMiddle_UpdatesAncestorMetadata()
    {
        var tree = new PieceTreeEngine("after");
        tree.Insert(0, "before");
        tree.Insert(6, "\r\n");

        Assert.Equal(8, tree.Root.SizeLeft);
        Assert.Equal(1, tree.Root.LFLeft);
        AssertMatchesText(tree, "before\r\nafter");
    }

    [Fact]
    public void Edits_ThroughCrLfBoundary_KeepLineCountsAndOffsets()
    {
        var tree = new PieceTreeEngine("first\r\na\r\nb");
        tree.Insert(9, "x");
        AssertMatchesText(tree, "first\r\na\rx\nb");

        tree.Delete(9, 1);
        AssertMatchesText(tree, "first\r\na\r\nb");

        tree.Insert(tree.Length, "\r");
        tree.Insert(tree.Length, "\n");
        AssertMatchesText(tree, "first\r\na\r\nb\r\n");
    }

    [Fact]
    public void RandomMixedLineEndingEdits_MatchPlainStringAndTreeInvariants()
    {
        string[] edits = ["a", "ab", "\r", "\n", "\r\n", "a\rb", "a\nb", "a\r\nb", "😀"];
        for (int seed = 0; seed < 64; seed++)
        {
            var random = new Random(seed);
            string text = "a\r\nb\rc\n";
            var tree = new PieceTreeEngine(text);
            for (int step = 0; step < 300; step++)
            {
                if (text.Length == 0 || random.Next(3) != 0)
                {
                    int offset = random.Next(text.Length + 1);
                    string edit = edits[random.Next(edits.Length)];
                    tree.Insert(offset, edit);
                    text = text.Insert(offset, edit);
                }
                else
                {
                    int offset = random.Next(text.Length);
                    int length = random.Next(1, text.Length - offset + 1);
                    tree.Delete(offset, length);
                    text = text.Remove(offset, length);
                }

                AssertMatchesText(tree, text);
            }
        }
    }

    private static void AssertMatchesText(PieceTreeEngine tree, string text)
    {
        Assert.Equal(text.Length, tree.Length);
        Assert.Equal(text, tree.GetTextInRange(0, tree.Length));
        var breaks = Regex.Matches(text, "\r\n|\r|\n");
        Assert.Equal(breaks.Count + 1, tree.LineCount);

        int start = 0;
        for (int line = 0; line < tree.LineCount; line++)
        {
            int end = line < breaks.Count ? breaks[line].Index : text.Length;
            int next = line < breaks.Count ? end + breaks[line].Length : text.Length + 1;
            Assert.Equal(text[start..end], tree.GetLineContent(line));
            Assert.Equal(start, tree.GetOffsetFromLineColumn(line, 0));
            for (int offset = start; offset < next; offset++)
            {
                Assert.Equal((line, offset - start), tree.GetLineColumnFromOffset(offset));
                if (offset < text.Length)
                    Assert.Equal(text.Substring(offset, 1), tree.GetTextInRange(offset, 1));
            }
            start = next;
        }
        AssertTree(tree);
    }

    private static void AssertTree(PieceTreeEngine tree)
    {
        Assert.Equal(NodeColor.Black, tree.Root.Color);
        var (length, breaks, _) = CheckNode(tree.Root, PieceTreeEngine.Sentinel);
        Assert.Equal(tree.Length, length);
        Assert.Equal(tree.LineCount, breaks + 1);
    }

    private static (int Length, int Breaks, int BlackHeight) CheckNode(TreeNode node, TreeNode parent)
    {
        if (node == PieceTreeEngine.Sentinel) return (0, 0, 1);

        Assert.Same(parent, node.Parent);
        Assert.True(node.Piece.Length > 0);
        if (node.Color == NodeColor.Red)
        {
            Assert.Equal(NodeColor.Black, node.Left.Color);
            Assert.Equal(NodeColor.Black, node.Right.Color);
        }

        var left = CheckNode(node.Left, node);
        var right = CheckNode(node.Right, node);
        Assert.Equal(left.BlackHeight, right.BlackHeight);
        Assert.Equal(left.Length, node.SizeLeft);
        Assert.Equal(left.Breaks, node.LFLeft);
        return (left.Length + node.Piece.Length + right.Length,
            left.Breaks + node.Piece.LineFeedCount + right.Breaks,
            left.BlackHeight + (node.Color == NodeColor.Black ? 1 : 0));
    }
}
