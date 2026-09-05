using System.Globalization;
using System.Text;
using Baudacious.Core.Terminal;
using Baudacious.Render;

namespace Baudacious.App;

/// <summary>
/// Paints a <see cref="Screen"/> onto the host console, repainting only rows that changed.
/// </summary>
/// <remarks>
/// A stand-in for the GPU renderer of Phase 3, and deliberately the same shape: it takes a
/// grid and a display setting and knows nothing about baud rates, shells or parsing. The
/// phosphor mapping is the real one — Rec. 709 luminance onto a single-colour ramp — so
/// the aesthetic can be judged before any OpenGL exists.
/// </remarks>
internal sealed class ConsoleRenderer
{
    private readonly StringBuilder _out = new(64 * 1024);
    private string[] _shadow = [];
    private int _shadowColumns;

    public Phosphor Phosphor { get; set; } = Phosphor.Green;

    public void Invalidate() => _shadow = [];

    public void Render(Screen screen, string status)
    {
        var rows = Math.Min(screen.RowCount, Math.Max(Console.WindowHeight - 1, 1));

        if (_shadow.Length != rows || _shadowColumns != screen.Columns)
        {
            _shadow = new string[rows];
            _shadowColumns = screen.Columns;
            _out.Append("\u001b[2J");
        }

        _out.Clear();
        _out.Append("\u001b[?25l");           // hide the cursor while painting

        for (var row = 0; row < rows; row++)
        {
            var rendered = RenderRow(screen, row);
            if (_shadow[row] == rendered)
            {
                continue;
            }

            _shadow[row] = rendered;
            _out.Append(string.Create(CultureInfo.InvariantCulture, $"\u001b[{row + 1};1H"));
            _out.Append(rendered);
            _out.Append("\u001b[0m\u001b[K");
        }

        // Status line: the backlog indicator specs/display-rendering asks for.
        _out.Append(string.Create(CultureInfo.InvariantCulture, $"\u001b[{rows + 1};1H"));
        _out.Append("\u001b[0m\u001b[7m");
        _out.Append(status.PadRight(Math.Max(Console.WindowWidth - 1, 1))[..Math.Max(Console.WindowWidth - 1, 1)]);
        _out.Append("\u001b[0m\u001b[K");

        // Put the real cursor where the emulator thinks it is.
        if (screen.CursorVisible && screen.CursorRow < rows)
        {
            _out.Append(string.Create(
                CultureInfo.InvariantCulture,
                $"\u001b[{screen.CursorRow + 1};{screen.CursorColumn + 1}H\u001b[?25h"));
        }

        Console.Out.Write(_out);
        Console.Out.Flush();
    }

    private string RenderRow(Screen screen, int row)
    {
        var builder = new StringBuilder(screen.Columns * 4);
        uint lastForeground = 0;
        uint lastBackground = 0;
        var lastFlags = CellFlags.None;
        var first = true;

        for (var column = 0; column < screen.Columns; column++)
        {
            var cell = screen.GetCell(row, column);
            var (foreground, background) = Map(cell);

            if (first || foreground != lastForeground || background != lastBackground || cell.Flags != lastFlags)
            {
                builder.Append("\u001b[0m");
                if (cell.Flags.HasFlag(CellFlags.Bold))
                {
                    builder.Append("\u001b[1m");
                }

                if (cell.Flags.HasFlag(CellFlags.Underline))
                {
                    builder.Append("\u001b[4m");
                }

                builder.Append(string.Create(
                    CultureInfo.InvariantCulture,
                    $"\u001b[38;2;{(foreground >> 24) & 0xFF};{(foreground >> 16) & 0xFF};{(foreground >> 8) & 0xFF}m"));
                builder.Append(string.Create(
                    CultureInfo.InvariantCulture,
                    $"\u001b[48;2;{(background >> 24) & 0xFF};{(background >> 16) & 0xFF};{(background >> 8) & 0xFF}m"));

                lastForeground = foreground;
                lastBackground = background;
                lastFlags = cell.Flags;
                first = false;
            }

            builder.Append(cell.Rune is 0 or '\0' ? ' ' : cell.Char);
        }

        return builder.ToString();
    }

    private (uint Foreground, uint Background) Map(Cell cell) =>
        PhosphorMap.Resolve(cell, Phosphor);

}
