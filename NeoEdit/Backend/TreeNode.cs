namespace Notepad.NeoEdit.Backend;

public enum NodeColor { Black, Red }
public struct NodePosition
{
    public TreeNode Node;
    public int StartOffset;
    public int Remainder;
}

public class TreeNode
{
    public TreeNode Parent;
    public TreeNode Left;
    public TreeNode Right;
    public NodeColor Color;
    public Piece Piece;
    public int SizeLeft;
    public int LFLeft;

    public TreeNode(Piece piece, NodeColor color)
    {
        Piece = piece;
        Color = color;
        SizeLeft = 0;
        LFLeft = 0;
        Parent = PieceTreeBase.Sentinel;
        Left = PieceTreeBase.Sentinel;
        Right = PieceTreeBase.Sentinel;
    }

    public TreeNode Next()
    {
        if (Right != PieceTreeBase.Sentinel)
        {
            TreeNode node = Right;
            while (node.Left != PieceTreeBase.Sentinel) node = node.Left;
            return node;
        }
        TreeNode p = Parent;
        TreeNode n = this;
        while (p != PieceTreeBase.Sentinel && n == p.Right)
        {
            n = p;
            p = p.Parent;
        }
        return p;
    }
}