using System.Text;
using Silk.NET.Input;

namespace Baudacious.App;

/// <summary>
/// Turns window key events into the bytes a shell expects.
/// </summary>
/// <remarks>
/// The rule from specs/input-and-local-commands is in force here: apart from Ctrl+C,
/// no plain Ctrl+letter chord is intercepted. They all belong to the shell.
/// </remarks>
public static class GlKeyEncoder
{
    /// <summary>Special keys. Printable characters arrive separately as KeyChar events.</summary>
    public static ReadOnlyMemory<byte> Encode(Key key, bool control, bool applicationCursorKeys)
    {
        const string Esc = "\u001b";
        var introducer = applicationCursorKeys ? Esc + "O" : Esc + "[";

        switch (key)
        {
            case Key.Up: return Ascii(introducer + "A");
            case Key.Down: return Ascii(introducer + "B");
            case Key.Right: return Ascii(introducer + "C");
            case Key.Left: return Ascii(introducer + "D");
            case Key.Home: return Ascii(introducer + "H");
            case Key.End: return Ascii(introducer + "F");

            case Key.PageUp: return Ascii(Esc + "[5~");
            case Key.PageDown: return Ascii(Esc + "[6~");
            case Key.Insert: return Ascii(Esc + "[2~");
            case Key.Delete: return Ascii(Esc + "[3~");

            case Key.F1: return Ascii(Esc + "OP");
            case Key.F2: return Ascii(Esc + "OQ");
            case Key.F3: return Ascii(Esc + "OR");
            case Key.F4: return Ascii(Esc + "OS");
            case Key.F5: return Ascii(Esc + "[15~");
            case Key.F6: return Ascii(Esc + "[17~");
            case Key.F7: return Ascii(Esc + "[18~");
            case Key.F8: return Ascii(Esc + "[19~");
            case Key.F9: return Ascii(Esc + "[20~");
            case Key.F10: return Ascii(Esc + "[21~");
            case Key.F11: return Ascii(Esc + "[23~");
            case Key.F12: return Ascii(Esc + "[24~");

            case Key.Enter or Key.KeypadEnter: return new byte[] { 0x0D };
            case Key.Backspace: return new byte[] { 0x7F };
            case Key.Tab: return new byte[] { 0x09 };
            case Key.Escape: return new byte[] { 0x1B };

            default:
                break;
        }

        if (control && key is >= Key.A and <= Key.Z)
        {
            return new[] { (byte)(key - Key.A + 1) };
        }

        return ReadOnlyMemory<byte>.Empty;
    }

    /// <summary>
    /// The ASCII byte an Alt chord should send after ESC, or null if the key has no
    /// sensible plain-text form.
    /// </summary>
    public static byte? MetaByte(Key key) => key switch
    {
        >= Key.A and <= Key.Z => (byte)('a' + (key - Key.A)),
        >= Key.Number0 and <= Key.Number9 => (byte)('0' + (key - Key.Number0)),
        Key.Space => (byte)' ',
        Key.Minus => (byte)'-',
        Key.Equal => (byte)'=',
        Key.Period => (byte)'.',
        Key.Comma => (byte)',',
        Key.Slash => (byte)'/',
        Key.BackSlash => (byte)'\\',
        Key.Semicolon => (byte)';',
        Key.Apostrophe => (byte)'\'',
        _ => null,
    };

    private static ReadOnlyMemory<byte> Ascii(string text) => Encoding.ASCII.GetBytes(text);
}
