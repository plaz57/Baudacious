using System.Globalization;
using System.Text;

namespace Baudacious.Core.Terminal;

/// <summary>
/// Turns parsed escape sequences into operations on a <see cref="Screen"/>.
/// </summary>
public sealed class TerminalEmulator : IVtHandler
{
    private readonly ITerminalHost _host;
    private readonly StringBuilder _title = new();

    public TerminalEmulator(Screen screen, ITerminalHost host)
    {
        ArgumentNullException.ThrowIfNull(screen);
        ArgumentNullException.ThrowIfNull(host);

        Screen = screen;
        _host = host;
    }

    public Screen Screen { get; }

    /// <summary>Set by DECCKM. Decides whether arrow keys send <c>ESC[A</c> or <c>ESC OA</c>.</summary>
    public bool ApplicationCursorKeys { get; private set; }

    /// <summary>Set by mode 2004. Paste is framed in markers when on.</summary>
    public bool BracketedPaste { get; private set; }

    /// <summary>Any of the mouse reporting modes.</summary>
    public bool MouseReporting { get; private set; }

    // ---- IVtHandler ---------------------------------------------------------

    public void Print(uint codepoint) => Screen.Print(codepoint);

    public void Execute(byte control)
    {
        switch (control)
        {
            case 0x07:
                _host.Bell();
                break;

            case 0x08:
                Screen.Backspace();
                break;

            case 0x09:
                Screen.Tab();
                break;

            case 0x0A or 0x0B or 0x0C:
                Screen.LineFeed();
                break;

            case 0x0D:
                Screen.CarriageReturn();
                break;

            default:
                break;
        }
    }

    public void EscDispatch(char final, ReadOnlySpan<char> intermediates)
    {
        // Character-set designations (ESC ( B and friends) are accepted and ignored: we
        // decode UTF-8 and have no use for the G0/G1 machinery.
        if (intermediates.Length > 0)
        {
            return;
        }

        switch (final)
        {
            case '7':
                Screen.SaveCursor();
                break;

            case '8':
                Screen.RestoreCursor();
                break;

            case 'D':
                Screen.LineFeed();
                break;

            case 'E':
                Screen.CarriageReturn();
                Screen.LineFeed();
                break;

            case 'M':
                Screen.ReverseLineFeed();
                break;

            case 'c':
                Screen.HardReset();
                break;

            default:
                break;
        }
    }

    public void OscDispatch(ReadOnlySpan<char> data)
    {
        // "0;title" sets icon name and title; "2;title" sets the title.
        var separator = data.IndexOf(';');
        if (separator < 0)
        {
            return;
        }

        var command = data[..separator];
        if (command is not ("0" or "2"))
        {
            return;
        }

        _title.Clear();
        _title.Append(data[(separator + 1)..]);
        _host.SetTitle(_title.ToString());
    }

    public void CsiDispatch(
        char final,
        ReadOnlySpan<int> parameters,
        uint colonMask,
        ReadOnlySpan<char> intermediates,
        bool isPrivate)
    {
        if (intermediates.Length > 0)
        {
            // DECSTR (ESC [ ! p) is the only intermediate form implemented.
            if (intermediates[0] == '!' && final == 'p')
            {
                SoftReset();
            }

            return;
        }

        if (isPrivate)
        {
            switch (final)
            {
                case 'h':
                    SetPrivateModes(parameters, enabled: true);
                    break;

                case 'l':
                    SetPrivateModes(parameters, enabled: false);
                    break;

                default:
                    break;
            }

            return;
        }

        switch (final)
        {
            case 'A': Screen.MoveBy(-Arg(parameters, 0, 1), 0); break;
            case 'B': Screen.MoveBy(Arg(parameters, 0, 1), 0); break;
            case 'C': Screen.MoveBy(0, Arg(parameters, 0, 1)); break;
            case 'D': Screen.MoveBy(0, -Arg(parameters, 0, 1)); break;

            case 'E':
                Screen.MoveBy(Arg(parameters, 0, 1), 0);
                Screen.SetColumn(0);
                break;

            case 'F':
                Screen.MoveBy(-Arg(parameters, 0, 1), 0);
                Screen.SetColumn(0);
                break;

            case 'G' or '`':
                Screen.SetColumn(Arg(parameters, 0, 1) - 1);
                break;

            case 'd':
                Screen.SetRow(Arg(parameters, 0, 1) - 1);
                break;

            case 'H' or 'f':
                Screen.MoveTo(Arg(parameters, 0, 1) - 1, Arg(parameters, 1, 1) - 1);
                break;

            case 'J': Screen.EraseInDisplay(Arg(parameters, 0, 0)); break;
            case 'K': Screen.EraseInLine(Arg(parameters, 0, 0)); break;
            case 'L': Screen.InsertLines(Arg(parameters, 0, 1)); break;
            case 'M': Screen.DeleteLines(Arg(parameters, 0, 1)); break;
            case 'P': Screen.DeleteCharacters(Arg(parameters, 0, 1)); break;
            case 'X': Screen.EraseCharacters(Arg(parameters, 0, 1)); break;
            case '@': Screen.InsertCharacters(Arg(parameters, 0, 1)); break;
            case 'S': Screen.ScrollUp(Arg(parameters, 0, 1)); break;
            case 'T': Screen.ScrollDown(Arg(parameters, 0, 1)); break;
            case 'I': Screen.Tab(Arg(parameters, 0, 1)); break;

            case 'Z':
                for (var i = 0; i < Arg(parameters, 0, 1); i++)
                {
                    BackTab();
                }

                break;

            case 'r':
                Screen.SetScrollRegion(
                    Arg(parameters, 0, 1) - 1,
                    Arg(parameters, 1, Screen.RowCount) - 1);
                break;

            case 's': Screen.SaveCursor(); break;
            case 'u': Screen.RestoreCursor(); break;

            case 'g':
                if (Arg(parameters, 0, 0) == 3)
                {
                    Screen.TabStops.Clear();
                }
                else
                {
                    Screen.TabStops.Remove(Screen.CursorColumn);
                }

                break;

            case 'm': ApplyGraphicRendition(parameters, colonMask); break;
            case 'n': DeviceStatusReport(Arg(parameters, 0, 0)); break;

            case 'c':
                // Claim a VT100 with the advanced video option, which is what
                // xterm-compatible hosts expect to hear back.
                Reply("[?1;2c");
                break;

            case 'h': SetAnsiModes(parameters, enabled: true); break;
            case 'l': SetAnsiModes(parameters, enabled: false); break;

            default:
                break;
        }
    }

    // ---- Modes --------------------------------------------------------------

    private void SetPrivateModes(ReadOnlySpan<int> parameters, bool enabled)
    {
        foreach (var mode in parameters)
        {
            switch (mode)
            {
                case 1:
                    ApplicationCursorKeys = enabled;
                    break;

                case 6:
                    Screen.OriginMode = enabled;
                    Screen.MoveTo(0, 0);
                    break;

                case 7:
                    Screen.AutoWrap = enabled;
                    break;

                case 25:
                    Screen.CursorVisible = enabled;
                    break;

                case 1000 or 1002 or 1003 or 1006 or 1015:
                    MouseReporting = enabled;
                    break;

                case 1047:
                    if (enabled)
                    {
                        Screen.EnterAlternate(clear: true);
                    }
                    else
                    {
                        Screen.LeaveAlternate();
                    }

                    break;

                case 1048:
                    if (enabled)
                    {
                        Screen.SaveCursor();
                    }
                    else
                    {
                        Screen.RestoreCursor();
                    }

                    break;

                case 1049:
                    if (enabled)
                    {
                        Screen.SaveCursor();
                        Screen.EnterAlternate(clear: true);
                        Screen.EraseInDisplay(2);
                    }
                    else
                    {
                        Screen.LeaveAlternate();
                        Screen.RestoreCursor();
                    }

                    break;

                case 2004:
                    BracketedPaste = enabled;
                    break;

                default:
                    break;
            }
        }
    }

    private void SetAnsiModes(ReadOnlySpan<int> parameters, bool enabled)
    {
        foreach (var mode in parameters)
        {
            // Mode 4 (insert/replace) and 20 (newline mode) are recognised and ignored;
            // nothing ConPTY emits depends on them.
            _ = mode;
            _ = enabled;
        }
    }

    private void SoftReset()
    {
        Screen.SetScrollRegion(0, Screen.RowCount - 1);
        Screen.OriginMode = false;
        Screen.AutoWrap = true;
        Screen.CursorVisible = true;
        Screen.Pen = Cell.Blank(Palette.DefaultForeground, Palette.DefaultBackground);
        ApplicationCursorKeys = false;
        BracketedPaste = false;
        MouseReporting = false;
    }

    private void BackTab()
    {
        var previous = 0;
        foreach (var stop in Screen.TabStops)
        {
            if (stop >= Screen.CursorColumn)
            {
                break;
            }

            previous = stop;
        }

        Screen.SetColumn(previous);
    }

    private void DeviceStatusReport(int what)
    {
        switch (what)
        {
            case 5:
                Reply("[0n");
                break;

            case 6:
                Reply(string.Create(
                    CultureInfo.InvariantCulture,
                    $"[{Screen.CursorRow + 1};{Screen.CursorColumn + 1}R"));
                break;

            default:
                break;
        }
    }

    private void Reply(string text)
    {
        Span<byte> buffer = stackalloc byte[64];
        var written = Encoding.ASCII.GetBytes(text, buffer);
        _host.Reply(buffer[..written]);
    }

    // ---- SGR ----------------------------------------------------------------

    private void ApplyGraphicRendition(ReadOnlySpan<int> parameters, uint colonMask)
    {
        if (parameters.IsEmpty)
        {
            ResetPen();
            return;
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            var code = parameters[i] < 0 ? 0 : parameters[i];
            var pen = Screen.Pen;

            switch (code)
            {
                case 0: ResetPen(); continue;
                case 1: pen.Flags |= CellFlags.Bold; break;
                case 2: pen.Flags |= CellFlags.Faint; break;
                case 3: pen.Flags |= CellFlags.Italic; break;
                case 4: pen.Flags |= CellFlags.Underline; break;
                case 5 or 6: pen.Flags |= CellFlags.Blink; break;
                case 7: pen.Flags |= CellFlags.Reverse; break;
                case 8: pen.Flags |= CellFlags.Invisible; break;
                case 9: pen.Flags |= CellFlags.Strikethrough; break;
                case 21 or 22: pen.Flags &= ~(CellFlags.Bold | CellFlags.Faint); break;
                case 23: pen.Flags &= ~CellFlags.Italic; break;
                case 24: pen.Flags &= ~CellFlags.Underline; break;
                case 25: pen.Flags &= ~CellFlags.Blink; break;
                case 27: pen.Flags &= ~CellFlags.Reverse; break;
                case 28: pen.Flags &= ~CellFlags.Invisible; break;
                case 29: pen.Flags &= ~CellFlags.Strikethrough; break;

                case >= 30 and <= 37: pen.Foreground = Palette.Ansi16[code - 30]; break;
                case >= 40 and <= 47: pen.Background = Palette.Ansi16[code - 40]; break;
                case >= 90 and <= 97: pen.Foreground = Palette.Ansi16[code - 90 + 8]; break;
                case >= 100 and <= 107: pen.Background = Palette.Ansi16[code - 100 + 8]; break;

                case 39: pen.Foreground = Palette.DefaultForeground; break;
                case 49: pen.Background = Palette.DefaultBackground; break;

                case 38 or 48:
                    if (TryReadExtendedColour(parameters, colonMask, ref i, out var colour))
                    {
                        if (code == 38)
                        {
                            pen.Foreground = colour;
                        }
                        else
                        {
                            pen.Background = colour;
                        }
                    }

                    break;

                default:
                    break;
            }

            Screen.Pen = pen;
        }
    }

    private void ResetPen() =>
        Screen.Pen = Cell.Blank(Palette.DefaultForeground, Palette.DefaultBackground);

    /// <summary>
    /// Reads the argument of a 38/48 extended-colour selector, advancing
    /// <paramref name="index"/> past what it consumed.
    /// </summary>
    /// <remarks>
    /// Two notations are in the wild: the semicolon form <c>38;2;r;g;b</c> that ConPTY and
    /// most programs emit, and the ISO 8613-6 colon form <c>38:2:cs:r:g:b</c>, whose
    /// colour-space id is usually empty. The colon mask says which separator preceded each
    /// parameter, which is what lets the six-element colon form be told apart from the
    /// five-element one.
    /// </remarks>
    private static bool TryReadExtendedColour(
        ReadOnlySpan<int> parameters, uint colonMask, ref int index, out uint colour)
    {
        colour = 0;
        if (index + 1 >= parameters.Length)
        {
            return false;
        }

        var kind = parameters[index + 1];
        var colonForm = index + 1 < 32 && (colonMask & (1u << (index + 1))) != 0;

        // How many parameters belong to this colon-separated group.
        var groupLength = 0;
        if (colonForm)
        {
            groupLength = 1;
            for (var j = index + 1; j < parameters.Length && j < 32; j++)
            {
                if ((colonMask & (1u << j)) == 0)
                {
                    break;
                }

                groupLength++;
            }
        }

        switch (kind)
        {
            case 5:
                if (index + 2 >= parameters.Length)
                {
                    return false;
                }

                colour = Palette.Indexed(Math.Max(parameters[index + 2], 0));
                index += colonForm ? groupLength - 1 : 2;
                return true;

            case 2:
                // Six in the group means a colour-space id sits at offset 2 and is skipped.
                var first = index + 2 + (colonForm && groupLength >= 6 ? 1 : 0);
                if (first + 2 >= parameters.Length)
                {
                    return false;
                }

                colour = Palette.Rgb(
                    Math.Max(parameters[first], 0),
                    Math.Max(parameters[first + 1], 0),
                    Math.Max(parameters[first + 2], 0));
                index = colonForm ? index + groupLength - 1 : first + 2;
                return true;

            default:
                return false;
        }
    }

    private static int Arg(ReadOnlySpan<int> parameters, int index, int fallback) =>
        index < parameters.Length && parameters[index] > 0 ? parameters[index] : fallback;
}
