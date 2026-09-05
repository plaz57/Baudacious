using System.Text;

namespace Baudacious.App;

/// <summary>
/// Turns a console key press into the bytes a shell expects.
/// </summary>
/// <remarks>
/// A preview of the Phase 4 input layer, limited to what <see cref="Console.ReadKey"/> can
/// report. The rule that matters is already in force: apart from Ctrl+C, no plain
/// Ctrl+letter chord is intercepted — they all belong to the shell.
/// </remarks>
internal static class KeyEncoder
{
    public static ReadOnlyMemory<byte> Encode(ConsoleKeyInfo key, bool applicationCursorKeys)
    {
        var control = key.Modifiers.HasFlag(ConsoleModifiers.Control);

        // Cursor keys change form under DECCKM, which is why the emulator's mode has to be
        // consulted rather than assumed.
        var introducer = applicationCursorKeys ? "\u001bO" : "\u001b[";

        switch (key.Key)
        {
            // introducer already carries the ESC. Prepending another sends ESC ESC [ A,
            // which the shell reads as an Escape keypress plus a stray sequence.
            case ConsoleKey.UpArrow: return Ascii(introducer + "A");
            case ConsoleKey.DownArrow: return Ascii(introducer + "B");
            case ConsoleKey.RightArrow: return Ascii(introducer + "C");
            case ConsoleKey.LeftArrow: return Ascii(introducer + "D");
            case ConsoleKey.Home: return Ascii(introducer + "H");
            case ConsoleKey.End: return Ascii(introducer + "F");

            case ConsoleKey.PageUp: return Ascii("\u001b[5~");
            case ConsoleKey.PageDown: return Ascii("\u001b[6~");
            case ConsoleKey.Insert: return Ascii("\u001b[2~");
            case ConsoleKey.Delete: return Ascii("\u001b[3~");

            case ConsoleKey.F1: return Ascii("\u001bOP");
            case ConsoleKey.F2: return Ascii("\u001bOQ");
            case ConsoleKey.F3: return Ascii("\u001bOR");
            case ConsoleKey.F4: return Ascii("\u001bOS");
            case ConsoleKey.F5: return Ascii("\u001b[15~");
            case ConsoleKey.F6: return Ascii("\u001b[17~");
            case ConsoleKey.F7: return Ascii("\u001b[18~");
            case ConsoleKey.F8: return Ascii("\u001b[19~");
            case ConsoleKey.F9: return Ascii("\u001b[20~");
            case ConsoleKey.F10: return Ascii("\u001b[21~");
            case ConsoleKey.F11: return Ascii("\u001b[23~");
            case ConsoleKey.F12: return Ascii("\u001b[24~");

            case ConsoleKey.Enter: return new byte[] { 0x0D };
            case ConsoleKey.Backspace: return new byte[] { 0x7F };
            case ConsoleKey.Tab: return new byte[] { 0x09 };
            case ConsoleKey.Escape: return new byte[] { 0x1B };

            default:
                break;
        }

        if (control && key.Key is >= ConsoleKey.A and <= ConsoleKey.Z)
        {
            return new[] { (byte)(key.Key - ConsoleKey.A + 1) };
        }

        return key.KeyChar == '\0'
            ? ReadOnlyMemory<byte>.Empty
            : Encoding.UTF8.GetBytes(key.KeyChar.ToString());
    }

    private static ReadOnlyMemory<byte> Ascii(string text) => Encoding.ASCII.GetBytes(text);
}
