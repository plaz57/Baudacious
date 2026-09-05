namespace Baudacious.Core.Terminal;

/// <summary>One line of cells.</summary>
/// <remarks>
/// <see cref="SoftWrapped"/> is the important field and the reason rows are objects rather
/// than slices of one big array. Whether a line break was forced by the text or merely by
/// the right margin cannot be recovered after the fact, and it is what makes both
/// reflow-on-resize and copying a wrapped line as a single line correct. Reflow is the
/// classic terminal bug farm; carrying the flag from the first commit is cheap insurance.
/// </remarks>
public sealed class Row
{
    public Row(int columns, Cell blank)
    {
        Cells = new Cell[columns];
        Clear(blank);
    }

    private Row(Cell[] cells, bool softWrapped)
    {
        Cells = cells;
        SoftWrapped = softWrapped;
    }

    public Cell[] Cells { get; private set; }

    /// <summary>True when this row runs into the next because it hit the right margin.</summary>
    public bool SoftWrapped { get; set; }

    public int Length => Cells.Length;

    public void Clear(Cell blank)
    {
        Cells.AsSpan().Fill(blank);
        SoftWrapped = false;
    }

    public Row Clone() => new((Cell[])Cells.Clone(), SoftWrapped);

    /// <summary>Index one past the last non-blank cell.</summary>
    public int TrimmedLength(Cell blank)
    {
        var end = Cells.Length;
        while (end > 0 && Cells[end - 1].Rune == blank.Rune && Cells[end - 1].Background == blank.Background)
        {
            end--;
        }

        return end;
    }

    public void Resize(int columns, Cell blank)
    {
        if (columns == Cells.Length)
        {
            return;
        }

        var replacement = new Cell[columns];
        replacement.AsSpan().Fill(blank);
        Cells.AsSpan(0, Math.Min(columns, Cells.Length)).CopyTo(replacement);
        Cells = replacement;
    }
}
