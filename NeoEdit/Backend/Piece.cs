namespace Notepad.NeoEdit.Backend;

public class Piece
{
    public readonly int BufferIndex;
    public readonly BufferCursor Start;
    public readonly BufferCursor End;
    public readonly int Length;
    public readonly int LineFeedCount;

    public Piece(int bufferIndex, BufferCursor start, BufferCursor end, int lineFeedCount, int length)
    {
        BufferIndex = bufferIndex;
        Start = start;
        End = end;
        LineFeedCount = lineFeedCount;
        Length = length;
    }
}