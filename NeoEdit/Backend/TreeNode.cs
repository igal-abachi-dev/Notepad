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
        Parent = PieceTreeEngine.Sentinel;
        Left = PieceTreeEngine.Sentinel;
        Right = PieceTreeEngine.Sentinel;
    }

    public TreeNode Next()
    {
        if (Right != PieceTreeEngine.Sentinel)
        {
            TreeNode node = Right;
            while (node.Left != PieceTreeEngine.Sentinel) node = node.Left;
            return node;
        }
        TreeNode p = Parent;
        TreeNode n = this;
        while (p != PieceTreeEngine.Sentinel && n == p.Right)
        {
            n = p;
            p = p.Parent;
        }
        return p;
    }
}