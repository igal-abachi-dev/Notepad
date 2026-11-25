using System;
using System.Collections.Generic;

namespace Notepad.NeoEdit;

/// <summary>
/// Tracks the extra height of measured wrapped lines. Unmeasured lines occupy one estimated row.
/// Sparse prefix sums let the editor find a viewport without shaping every earlier line.
/// </summary>
internal sealed class WrappedLineMetrics(int lineCount)
{
    private Dictionary<int, double> _extraHeights = new();
    private readonly Dictionary<int, double> _prefixSums = new();
    private int _lineCount = lineCount;

    public double TopOfLine(int line, double rowHeight)
    {
        double top = line * rowHeight;
        for (int i = line; i > 0; i -= i & -i)
            top += _prefixSums.GetValueOrDefault(i);
        return top;
    }

    public void SetHeight(int line, double extraHeight)
    {
        double change = extraHeight - _extraHeights.GetValueOrDefault(line);
        if (change == 0) return;
        if (extraHeight > 0) _extraHeights[line] = extraHeight;
        else _extraHeights.Remove(line);

        for (long i = line + 1L; i <= _lineCount; i += i & -i)
        {
            int node = (int)i;
            double total = _prefixSums.GetValueOrDefault(node) + change;
            if (Math.Abs(total) < 0.00001) _prefixSums.Remove(node);
            else _prefixSums[node] = total;
        }
    }

    public void ApplyEdit(int start, int oldLast, int newCount)
    {
        int delta = newCount - _lineCount;
        if (delta == 0)
        {
            // Large replacements are cheaper to resolve against the measured entries.
            if (oldLast - start + 1 > _extraHeights.Count)
            {
                foreach (int measured in new List<int>(_extraHeights.Keys))
                    if (measured >= start && measured <= oldLast) SetHeight(measured, 0);
            }
            else
            {
                for (int line = start; line <= oldLast; line++)
                    SetHeight(line, 0);
            }
            return;
        }

        var previous = _extraHeights;
        _extraHeights = new();
        _prefixSums.Clear();
        _lineCount = newCount;
        foreach (var (line, extraHeight) in previous)
        {
            if (line < start)
                SetHeight(line, extraHeight);
            else if (line > oldLast)
                SetHeight(line + delta, extraHeight);
        }
    }
}
