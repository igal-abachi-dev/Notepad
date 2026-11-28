using System;

namespace Notepad.WindowsAutomation;

public enum TextUnit { Character, Format, Word, Line, Paragraph, Page, Document }

public readonly record struct TextSpan(int Start, int End)
{
    public int Length => End - Start;
    public bool IsEmpty => Start == End;
}

public abstract class EditorDocument
{
    public abstract T OnUiThread<T>(Func<T> action);
    public abstract int Length { get; }
    public abstract bool IsFocused { get; }
    public abstract bool IsEnabled { get; }
    public virtual bool IsReadOnly => !IsEnabled;
    public abstract TextSpan Selection { get; }
    public abstract int Caret { get; }
    public abstract ScreenRectangle Bounds { get; }
    public abstract string GetText(TextSpan range);
    public abstract TextSpan UnitAt(int offset, TextUnit unit);
    public abstract TextSpan[] GetVisibleRanges();
    public abstract ScreenRectangle[] GetBounds(TextSpan range);
    public abstract int HitTest(double x, double y);
    public abstract void Select(TextSpan range);
    public abstract void ScrollIntoView(TextSpan range, bool alignToTop);
    public abstract void Focus();
    public abstract void SetText(string value);
}
