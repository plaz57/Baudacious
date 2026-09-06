using System.Text;

namespace Baudacious.Core.Terminal;

/// <summary>
/// The character grid: cursor, scroll region, scrollback, and the alternate buffer.
/// </summary>
public sealed class Screen
{
    private readonly List<Row> _scrollback = [];

    private Row[] _rows;
    private Row[]? _savedPrimary;
    private int _savedPrimaryCursorRow;
    private int _savedPrimaryCursorColumn;

    public Screen(int columns, int rows, int scrollbackCapacity = 10_000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);

        Columns = columns;
        RowCount = rows;
        ScrollbackCapacity = scrollbackCapacity;

        _rows = new Row[rows];
        for (var i = 0; i < rows; i++)
        {
            _rows[i] = new Row(columns, Blank);
        }

        ScrollBottom = rows - 1;
    }

    public int Columns { get; private set; }

    public int RowCount { get; private set; }

    public int ScrollbackCapacity { get; set; }

    public int CursorRow { get; private set; }

    public int CursorColumn { get; private set; }

    public bool CursorVisible { get; set; } = true;

    public bool AutoWrap { get; set; } = true;

    public bool OriginMode { get; set; }

    /// <summary>Top row of the scrolling region, 0-based inclusive.</summary>
    public int ScrollTop { get; private set; }

    /// <summary>Bottom row of the scrolling region, 0-based inclusive.</summary>
    public int ScrollBottom { get; private set; }

    public bool IsAlternate => _savedPrimary is not null;

    /// <summary>Current graphic rendition — the attributes newly written cells inherit.</summary>
    public Cell Pen { get; set; } = Cell.Blank(Palette.DefaultForeground, Palette.DefaultBackground);

    public IReadOnlyList<Row> Scrollback => _scrollback;

    /// <summary>
    /// How many lines have ever scrolled off the top, including ones since dropped from
    /// the buffer.
    /// </summary>
    /// <remarks>
    /// Monotonic, so it can number lines absolutely. A selection anchored to an absolute
    /// line survives new output arriving and old lines being evicted, which a selection
    /// anchored to a scrollback index would not.
    /// </remarks>
    public long ScrolledLines { get; private set; }

    /// <summary>
    /// How far the view is scrolled back, in lines. Zero is the live view.
    /// </summary>
    /// <remarks>
    /// Only the renderer consults this. The emulator keeps writing to the live grid
    /// regardless, so output arriving while the user is reading history neither moves the
    /// view nor is lost.
    /// </remarks>
    public int ViewOffset { get; private set; }

    public bool IsScrolledBack => ViewOffset > 0;

    /// <summary>Absolute line number of a row in the current view.</summary>
    public long AbsoluteRowOf(int viewRow) => ScrolledLines - ViewOffset + viewRow;

    /// <summary>Scrolls the view. Positive moves into history; clamped to what exists.</summary>
    public void ScrollView(int lines) => SetViewOffset(ViewOffset + lines);

    public void SetViewOffset(int offset) =>
        ViewOffset = Math.Clamp(offset, 0, _scrollback.Count);

    /// <summary>Returns to the live view.</summary>
    public void ScrollToLive() => ViewOffset = 0;

    /// <summary>
    /// The cell shown at a view position, which is a scrollback line when scrolled back.
    /// </summary>
    public Cell GetViewCell(int row, int column)
    {
        if (column < 0 || column >= Columns || row < 0 || row >= RowCount)
        {
            return default;
        }

        var index = row - ViewOffset;
        if (index >= 0)
        {
            return _rows[index].Cells[column];
        }

        var line = _scrollback[_scrollback.Count + index];
        return column < line.Length ? line.Cells[column] : default;
    }

    /// <summary>
    /// Finds a row by absolute line number, wherever it currently lives.
    /// </summary>
    /// <returns>False when the line has been evicted from history or is not yet written.</returns>
    public bool TryGetAbsoluteRow(long absolute, out Row row)
    {
        var offsetFromScreen = absolute - ScrolledLines;

        if (offsetFromScreen >= 0)
        {
            if (offsetFromScreen < RowCount)
            {
                row = _rows[(int)offsetFromScreen];
                return true;
            }
        }
        else
        {
            var index = _scrollback.Count + (int)offsetFromScreen;
            if (index >= 0 && index < _scrollback.Count)
            {
                row = _scrollback[index];
                return true;
            }
        }

        row = null!;
        return false;
    }

    /// <summary>The row shown at a view position, for text extraction.</summary>
    public Row GetViewRow(int row)
    {
        var index = row - ViewOffset;
        return index >= 0 ? _rows[index] : _scrollback[_scrollback.Count + index];
    }

    /// <summary>
    /// Deferred wrap. After printing into the last column the cursor stays put and this is
    /// set; the next printable character wraps first. Without it, writing exactly one
    /// screen-width of text would scroll the display a line early.
    /// </summary>
    private bool _pendingWrap;

    private Cell Blank => Cell.Blank(Pen.Foreground, Pen.Background);

    public Row this[int row] => _rows[Math.Clamp(row, 0, RowCount - 1)];

    public Cell GetCell(int row, int column) =>
        row >= 0 && row < RowCount && column >= 0 && column < Columns
            ? _rows[row].Cells[column]
            : default;

    // ---- Writing ------------------------------------------------------------

    public void Print(uint codepoint)
    {
        if (_pendingWrap && AutoWrap)
        {
            _rows[CursorRow].SoftWrapped = true;
            CursorColumn = 0;
            LineFeed();
        }

        _pendingWrap = false;

        if (CursorColumn >= Columns)
        {
            CursorColumn = Columns - 1;
        }

        var cell = Pen;
        cell.Rune = codepoint;
        _rows[CursorRow].Cells[CursorColumn] = cell;

        if (CursorColumn == Columns - 1)
        {
            _pendingWrap = true;
        }
        else
        {
            CursorColumn++;
        }
    }

    public void CarriageReturn()
    {
        CursorColumn = 0;
        _pendingWrap = false;
    }

    public void LineFeed()
    {
        _pendingWrap = false;

        if (CursorRow == ScrollBottom)
        {
            ScrollUp(1);
        }
        else if (CursorRow < RowCount - 1)
        {
            CursorRow++;
        }
    }

    public void ReverseLineFeed()
    {
        _pendingWrap = false;

        if (CursorRow == ScrollTop)
        {
            ScrollDown(1);
        }
        else if (CursorRow > 0)
        {
            CursorRow--;
        }
    }

    public void Backspace()
    {
        _pendingWrap = false;
        if (CursorColumn > 0)
        {
            CursorColumn--;
        }
    }

    public void Tab(int count = 1)
    {
        for (var i = 0; i < count; i++)
        {
            var next = TabStops.Where(stop => stop > CursorColumn).DefaultIfEmpty(Columns - 1).First();
            CursorColumn = Math.Min(next, Columns - 1);
        }

        _pendingWrap = false;
    }

    /// <summary>Tab stops, default every eight columns.</summary>
    public SortedSet<int> TabStops { get; } = [.. Enumerable.Range(0, 512).Where(i => i % 8 == 0 && i > 0)];

    // ---- Cursor -------------------------------------------------------------

    public void MoveTo(int row, int column)
    {
        var top = OriginMode ? ScrollTop : 0;
        var bottom = OriginMode ? ScrollBottom : RowCount - 1;

        CursorRow = Math.Clamp(row + (OriginMode ? ScrollTop : 0), top, bottom);
        CursorColumn = Math.Clamp(column, 0, Columns - 1);
        _pendingWrap = false;
    }

    public void MoveBy(int rowDelta, int columnDelta) =>
        MoveTo(CursorRow - (OriginMode ? ScrollTop : 0) + rowDelta, CursorColumn + columnDelta);

    public void SetColumn(int column)
    {
        CursorColumn = Math.Clamp(column, 0, Columns - 1);
        _pendingWrap = false;
    }

    public void SetRow(int row) => MoveTo(row, CursorColumn);

    private int _savedRow;
    private int _savedColumn;
    private Cell _savedPen;

    public void SaveCursor()
    {
        _savedRow = CursorRow;
        _savedColumn = CursorColumn;
        _savedPen = Pen;
    }

    public void RestoreCursor()
    {
        Pen = _savedPen.Rune == 0 ? Pen : _savedPen;
        MoveTo(_savedRow, _savedColumn);
    }

    // ---- Scrolling ----------------------------------------------------------

    public void SetScrollRegion(int top, int bottom)
    {
        top = Math.Clamp(top, 0, RowCount - 1);
        bottom = Math.Clamp(bottom, 0, RowCount - 1);

        if (top >= bottom)
        {
            top = 0;
            bottom = RowCount - 1;
        }

        ScrollTop = top;
        ScrollBottom = bottom;
        MoveTo(0, 0);
    }

    public void ScrollUp(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var evicted = _rows[ScrollTop];

            // Only a full-height region feeds scrollback, and never from the alternate
            // buffer: a full-screen program must not pollute the history behind it.
            if (ScrollTop == 0 && ScrollBottom == RowCount - 1 && !IsAlternate)
            {
                PushScrollback(evicted.Clone());
            }

            for (var row = ScrollTop; row < ScrollBottom; row++)
            {
                _rows[row] = _rows[row + 1];
            }

            evicted.Clear(Blank);
            _rows[ScrollBottom] = evicted;
        }
    }

    public void ScrollDown(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var recycled = _rows[ScrollBottom];
            for (var row = ScrollBottom; row > ScrollTop; row--)
            {
                _rows[row] = _rows[row - 1];
            }

            recycled.Clear(Blank);
            _rows[ScrollTop] = recycled;
        }
    }

    private void PushScrollback(Row row)
    {
        _scrollback.Add(row);
        ScrolledLines++;

        if (_scrollback.Count > ScrollbackCapacity)
        {
            _scrollback.RemoveRange(0, _scrollback.Count - ScrollbackCapacity);
        }

        // A reader scrolled back stays looking at the same text as new output arrives,
        // rather than being dragged along by it.
        if (ViewOffset > 0)
        {
            ViewOffset = Math.Min(ViewOffset + 1, _scrollback.Count);
        }
    }

    // ---- Erasing and editing -------------------------------------------------

    public void EraseInDisplay(int mode)
    {
        switch (mode)
        {
            case 0:                                   // cursor to end
                EraseInLine(0);
                for (var row = CursorRow + 1; row < RowCount; row++)
                {
                    _rows[row].Clear(Blank);
                }

                break;

            case 1:                                   // start to cursor
                for (var row = 0; row < CursorRow; row++)
                {
                    _rows[row].Clear(Blank);
                }

                EraseInLine(1);
                break;

            case 2:                                   // all
            case 3:                                   // all plus scrollback
                foreach (var row in _rows)
                {
                    row.Clear(Blank);
                }

                if (mode == 3)
                {
                    _scrollback.Clear();
                }

                break;

            default:
                break;
        }

        _pendingWrap = false;
    }

    public void EraseInLine(int mode)
    {
        var cells = _rows[CursorRow].Cells;
        switch (mode)
        {
            case 0:
                cells.AsSpan(CursorColumn).Fill(Blank);
                _rows[CursorRow].SoftWrapped = false;
                break;

            case 1:
                cells.AsSpan(0, Math.Min(CursorColumn + 1, Columns)).Fill(Blank);
                break;

            case 2:
                _rows[CursorRow].Clear(Blank);
                break;

            default:
                break;
        }

        _pendingWrap = false;
    }

    public void EraseCharacters(int count)
    {
        var end = Math.Min(CursorColumn + count, Columns);
        _rows[CursorRow].Cells.AsSpan(CursorColumn, end - CursorColumn).Fill(Blank);
        _pendingWrap = false;
    }

    public void InsertLines(int count)
    {
        if (CursorRow < ScrollTop || CursorRow > ScrollBottom)
        {
            return;
        }

        for (var i = 0; i < count; i++)
        {
            var recycled = _rows[ScrollBottom];
            for (var row = ScrollBottom; row > CursorRow; row--)
            {
                _rows[row] = _rows[row - 1];
            }

            recycled.Clear(Blank);
            _rows[CursorRow] = recycled;
        }

        CursorColumn = 0;
    }

    public void DeleteLines(int count)
    {
        if (CursorRow < ScrollTop || CursorRow > ScrollBottom)
        {
            return;
        }

        for (var i = 0; i < count; i++)
        {
            var evicted = _rows[CursorRow];
            for (var row = CursorRow; row < ScrollBottom; row++)
            {
                _rows[row] = _rows[row + 1];
            }

            evicted.Clear(Blank);
            _rows[ScrollBottom] = evicted;
        }

        CursorColumn = 0;
    }

    public void InsertCharacters(int count)
    {
        var cells = _rows[CursorRow].Cells;
        count = Math.Min(count, Columns - CursorColumn);
        if (count <= 0)
        {
            return;
        }

        for (var column = Columns - 1; column >= CursorColumn + count; column--)
        {
            cells[column] = cells[column - count];
        }

        cells.AsSpan(CursorColumn, count).Fill(Blank);
        _pendingWrap = false;
    }

    public void DeleteCharacters(int count)
    {
        var cells = _rows[CursorRow].Cells;
        count = Math.Min(count, Columns - CursorColumn);
        if (count <= 0)
        {
            return;
        }

        for (var column = CursorColumn; column < Columns - count; column++)
        {
            cells[column] = cells[column + count];
        }

        cells.AsSpan(Columns - count, count).Fill(Blank);
        _pendingWrap = false;
    }

    // ---- Alternate buffer ---------------------------------------------------

    public void EnterAlternate(bool clear)
    {
        if (IsAlternate)
        {
            return;
        }

        _savedPrimary = _rows;
        _savedPrimaryCursorRow = CursorRow;
        _savedPrimaryCursorColumn = CursorColumn;

        _rows = new Row[RowCount];
        for (var i = 0; i < RowCount; i++)
        {
            _rows[i] = new Row(Columns, Blank);
        }

        if (clear)
        {
            MoveTo(0, 0);
        }
    }

    public void LeaveAlternate()
    {
        if (_savedPrimary is null)
        {
            return;
        }

        _rows = _savedPrimary;
        _savedPrimary = null;
        CursorRow = Math.Min(_savedPrimaryCursorRow, RowCount - 1);
        CursorColumn = Math.Min(_savedPrimaryCursorColumn, Columns - 1);
        _pendingWrap = false;
    }

    // ---- Resize with reflow --------------------------------------------------

    /// <summary>
    /// Resizes the grid, re-wrapping soft-wrapped lines to the new width and preserving
    /// hard line breaks.
    /// </summary>
    public void Resize(int columns, int rows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);

        if (columns == Columns && rows == RowCount)
        {
            return;
        }

        if (IsAlternate)
        {
            // A full-screen program owns the alternate buffer and will repaint it; there
            // is nothing meaningful to reflow.
            ResizeAlternate(columns, rows);
            return;
        }

        var blank = Blank;

        // Distance from the cursor back to the start of its logical line, so the cursor
        // can be put back on the same text after re-wrapping.
        var logical = ToLogicalLines(out var cursorLine, out var cursorOffset);

        Columns = columns;
        RowCount = rows;

        var rewrapped = new List<Row>();
        var newCursorRow = 0;
        var newCursorColumn = 0;

        for (var lineIndex = 0; lineIndex < logical.Count; lineIndex++)
        {
            var line = logical[lineIndex];
            var start = rewrapped.Count;
            var produced = WrapLine(line, columns, blank, rewrapped);

            if (lineIndex == cursorLine)
            {
                var offset = Math.Min(cursorOffset, Math.Max(produced * columns, 1) - 1);
                newCursorRow = start + (offset / columns);
                newCursorColumn = offset % columns;
            }
        }

        while (rewrapped.Count < rows)
        {
            rewrapped.Add(new Row(columns, blank));
        }

        // Everything above the last `rows` lines becomes scrollback.
        var keepFrom = Math.Max(0, rewrapped.Count - rows);
        _scrollback.Clear();
        for (var i = 0; i < keepFrom; i++)
        {
            _scrollback.Add(rewrapped[i]);
        }

        if (_scrollback.Count > ScrollbackCapacity)
        {
            _scrollback.RemoveRange(0, _scrollback.Count - ScrollbackCapacity);
        }

        _rows = new Row[rows];
        for (var i = 0; i < rows; i++)
        {
            _rows[i] = rewrapped[keepFrom + i];
        }

        ScrollTop = 0;
        ScrollBottom = rows - 1;
        ViewOffset = 0;
        CursorRow = Math.Clamp(newCursorRow - keepFrom, 0, rows - 1);
        CursorColumn = Math.Clamp(newCursorColumn, 0, columns - 1);
        _pendingWrap = false;
    }

    private void ResizeAlternate(int columns, int rows)
    {
        var blank = Blank;

        // The saved primary has to be resized to the same shape, not merely re-widened.
        // Leaving it at the old row count means that on the way back out of the alternate
        // buffer the grid is shorter than RowCount claims, and the next read runs off the
        // end of the array.
        _rows = ResizeRowArray(_rows, columns, rows, blank);
        if (_savedPrimary is not null)
        {
            _savedPrimary = ResizeRowArray(_savedPrimary, columns, rows, blank);
            _savedPrimaryCursorRow = Math.Min(_savedPrimaryCursorRow, rows - 1);
            _savedPrimaryCursorColumn = Math.Min(_savedPrimaryCursorColumn, columns - 1);
        }

        Columns = columns;
        RowCount = rows;
        ScrollTop = 0;
        ScrollBottom = rows - 1;
        CursorRow = Math.Min(CursorRow, rows - 1);
        CursorColumn = Math.Min(CursorColumn, columns - 1);
    }

    /// <summary>
    /// Re-shapes a row array without reflowing. Shrinking keeps the bottom rows, since
    /// that is where the recent output is.
    /// </summary>
    private static Row[] ResizeRowArray(Row[] source, int columns, int rows, Cell blank)
    {
        var result = new Row[rows];
        var offset = Math.Max(0, source.Length - rows);

        for (var i = 0; i < rows; i++)
        {
            var index = offset + i;
            result[i] = index < source.Length ? source[index] : new Row(columns, blank);
            result[i].Resize(columns, blank);
        }

        return result;
    }

    /// <summary>
    /// Flattens scrollback plus screen into logical lines, joining runs of soft-wrapped
    /// rows. Reports which logical line the cursor is on and how far along it.
    /// </summary>
    private List<List<Cell>> ToLogicalLines(out int cursorLine, out int cursorOffset)
    {
        var lines = new List<List<Cell>>();
        var current = new List<Cell>();
        var started = false;

        cursorLine = 0;
        cursorOffset = 0;

        var all = new List<(Row Row, bool OnScreen, int Index)>();
        foreach (var row in _scrollback)
        {
            all.Add((row, false, 0));
        }

        for (var i = 0; i < RowCount; i++)
        {
            all.Add((_rows[i], true, i));
        }

        foreach (var (row, onScreen, index) in all)
        {
            if (!started)
            {
                started = true;
            }

            if (onScreen && index == CursorRow)
            {
                cursorLine = lines.Count;
                cursorOffset = current.Count + CursorColumn;
            }

            var take = row.SoftWrapped ? row.Length : row.TrimmedLength(Blank);
            for (var column = 0; column < take; column++)
            {
                current.Add(row.Cells[column]);
            }

            if (!row.SoftWrapped)
            {
                lines.Add(current);
                current = [];
            }
        }

        if (current.Count > 0 || lines.Count == 0)
        {
            lines.Add(current);
        }

        return lines;
    }

    private static int WrapLine(List<Cell> line, int columns, Cell blank, List<Row> output)
    {
        if (line.Count == 0)
        {
            output.Add(new Row(columns, blank));
            return 1;
        }

        var produced = 0;
        for (var offset = 0; offset < line.Count; offset += columns)
        {
            var row = new Row(columns, blank);
            var count = Math.Min(columns, line.Count - offset);
            for (var i = 0; i < count; i++)
            {
                row.Cells[i] = line[offset + i];
            }

            row.SoftWrapped = offset + columns < line.Count;
            output.Add(row);
            produced++;
        }

        return produced;
    }

    // ---- Inspection ----------------------------------------------------------

    /// <summary>Renders the visible grid as plain text, for tests and the console harness.</summary>
    public string ToText(bool trimTrailing = true)
    {
        var builder = new StringBuilder();
        for (var row = 0; row < RowCount; row++)
        {
            var line = RowText(row);
            builder.Append(trimTrailing ? line.TrimEnd() : line);
            if (row < RowCount - 1)
            {
                builder.Append('\n');
            }
        }

        return builder.ToString();
    }

    public string RowText(int row)
    {
        var builder = new StringBuilder(Columns);
        foreach (var cell in _rows[row].Cells)
        {
            builder.Append(cell.Rune == 0 ? ' ' : cell.Char);
        }

        return builder.ToString();
    }

    public string ScrollbackText()
    {
        var builder = new StringBuilder();
        foreach (var row in _scrollback)
        {
            var line = new StringBuilder(row.Length);
            foreach (var cell in row.Cells)
            {
                line.Append(cell.Rune == 0 ? ' ' : cell.Char);
            }

            builder.Append(line.ToString().TrimEnd()).Append('\n');
        }

        return builder.ToString();
    }

    public void HardReset()
    {
        _savedPrimary = null;
        _rows = new Row[RowCount];
        for (var i = 0; i < RowCount; i++)
        {
            _rows[i] = new Row(Columns, Cell.Blank(Palette.DefaultForeground, Palette.DefaultBackground));
        }

        _scrollback.Clear();
        ViewOffset = 0;
        Pen = Cell.Blank(Palette.DefaultForeground, Palette.DefaultBackground);
        CursorRow = 0;
        CursorColumn = 0;
        CursorVisible = true;
        AutoWrap = true;
        OriginMode = false;
        ScrollTop = 0;
        ScrollBottom = RowCount - 1;
        _pendingWrap = false;
    }
}
