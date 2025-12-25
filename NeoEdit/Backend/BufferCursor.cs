namespace Notepad.NeoEdit.Backend;

public readonly struct BufferCursor
{
    public readonly int Line;
    public readonly int Column;
    public BufferCursor(int line, int column) { Line = line; Column = column; }
    public override string ToString() => $"({Line},{Column})";
}