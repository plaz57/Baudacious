using System.Text;

namespace Baudacious.Core.Terminal;

/// <summary>A cell, addressed by absolute line number rather than by screen row.</summary>
/// <remarks>
/// Absolute so that a selection survives output arriving and history being evicted. A
/// selection anchored to a screen row would slide up the text as the display scrolled.
/// </remarks>
public readonly record struct GridPoint(long Line, int Column) : IComparable<GridPoint>
{
    public int CompareTo(GridPoint other)
    {
        var byLine = Line.CompareTo(other.Line);
        return byLine != 0 ? byLine : Column.CompareTo(other.Column);
    }

    public static bool operator <(GridPoint a, GridPoint b) => a.CompareTo(b) < 0;

    public static bool operator >(GridPoint a, GridPoint b) => a.CompareTo(b) > 0;

    public static bool operator <=(GridPoint a, GridPoint b) => a.CompareTo(b) <= 0;

    public static bool operator >=(GridPoint a, GridPoint b) => a.CompareTo(b) >= 0;
}

public enum SelectionMode
{
    Character,
    Word,
    Line,
}

/// <summary>A run of selected text, from an anchor to a moving end.</summary>
public sealed record Selection(GridPoint Anchor, GridPoint Focus, SelectionMode Mode = SelectionMode.Character)
{
    public GridPoint Start => Anchor <= Focus ? Anchor : Focus;

    public GridPoint End => Anchor <= Focus ? Focus : Anchor;

    public bool IsEmpty => Start.Equals(End);

    public bool Contains(long line, int column)
    {
        var point = new GridPoint(line, column);
        return point >= Start && point < End;
    }

    /// <summary>
    /// Pulls the selected text out of the screen.
    /// </summary>
    /// <remarks>
    /// A soft-wrapped line comes back as one line, because that is what it is — the break
    /// was the right margin's doing, not the text's. Trailing blanks on each hard line are
    /// dropped, since the cells to the right of the last character were never written to.
    /// </remarks>
    public string GetText(Screen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);

        if (IsEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var line = new StringBuilder();

        for (var absolute = Start.Line; absolute <= End.Line; absolute++)
        {
            if (!screen.TryGetAbsoluteRow(absolute, out var row))
            {
                continue;
            }

            var from = absolute == Start.Line ? Start.Column : 0;
            var to = absolute == End.Line ? End.Column : row.Length;

            line.Clear();
            for (var column = from; column < Math.Min(to, row.Length); column++)
            {
                var rune = row.Cells[column].Rune;
                line.Append(rune == 0 ? ' ' : (char)Math.Min(rune, char.MaxValue));
            }

            if (row.SoftWrapped && absolute != End.Line)
            {
                // Continues onto the next row; no break, and no trimming — the character
                // at the margin is real.
                builder.Append(line);
            }
            else
            {
                builder.Append(line.ToString().TrimEnd());
                if (absolute != End.Line)
                {
                    builder.Append('\n');
                }
            }
        }

        return builder.ToString();
    }

    /// <summary>Grows a click into the word or line it landed on.</summary>
    public static Selection Expand(Screen screen, GridPoint point, SelectionMode mode)
    {
        ArgumentNullException.ThrowIfNull(screen);

        if (mode == SelectionMode.Character || !screen.TryGetAbsoluteRow(point.Line, out var row))
        {
            return new Selection(point, point, mode);
        }

        if (mode == SelectionMode.Line)
        {
            return new Selection(
                new GridPoint(point.Line, 0),
                new GridPoint(point.Line, row.Length),
                mode);
        }

        static bool IsWord(uint rune) =>
            rune != 0 && rune != ' ' && !char.IsPunctuation((char)Math.Min(rune, char.MaxValue));

        var column = Math.Clamp(point.Column, 0, row.Length - 1);
        if (!IsWord(row.Cells[column].Rune))
        {
            return new Selection(point, new GridPoint(point.Line, column + 1), mode);
        }

        var start = column;
        while (start > 0 && IsWord(row.Cells[start - 1].Rune))
        {
            start--;
        }

        var end = column;
        while (end + 1 < row.Length && IsWord(row.Cells[end + 1].Rune))
        {
            end++;
        }

        return new Selection(new GridPoint(point.Line, start), new GridPoint(point.Line, end + 1), mode);
    }
}
