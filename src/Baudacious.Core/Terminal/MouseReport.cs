using System.Globalization;
using System.Text;

namespace Baudacious.Core.Terminal;

public enum MouseButton
{
    Left = 0,
    Middle = 1,
    Right = 2,
    WheelUp = 64,
    WheelDown = 65,
}

/// <summary>
/// Builds the byte sequences a hosted program expects when it has asked for mouse
/// reporting.
/// </summary>
public static class MouseReport
{
    /// <summary>
    /// Encodes one event, or null when the current tracking level does not want it.
    /// </summary>
    /// <param name="column">Zero-based; the wire format is one-based and this converts.</param>
    /// <param name="row">Zero-based, likewise.</param>
    public static string? Encode(
        MouseTracking tracking,
        MouseEncoding encoding,
        MouseButton button,
        bool pressed,
        bool motion,
        int column,
        int row,
        bool shift = false,
        bool alt = false,
        bool control = false)
    {
        if (tracking == MouseTracking.Off)
        {
            return null;
        }

        // Click tracking wants presses and releases but not movement; drag tracking adds
        // movement while a button is down; motion tracking adds movement with none.
        if (motion && tracking == MouseTracking.Click)
        {
            return null;
        }

        var code = (int)button;

        if (motion)
        {
            code += 32;
        }

        if (shift)
        {
            code += 4;
        }

        if (alt)
        {
            code += 8;
        }

        if (control)
        {
            code += 16;
        }

        // Coordinates go out one-based, as the terminal has always numbered them.
        var x = column + 1;
        var y = row + 1;

        if (encoding == MouseEncoding.Sgr)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"\u001b[<{code};{x};{y}{(pressed ? 'M' : 'm')}");
        }

        // The legacy form has no way to say which button was released, so every release is
        // reported as button 3.
        if (!pressed)
        {
            code = 3 + (code & ~3);
        }

        // Each field is its value plus 32, which is why this form stops at 223.
        if (x > 223 || y > 223)
        {
            return null;
        }

        var builder = new StringBuilder(6);
        builder.Append('\u001b').Append("[M");
        builder.Append((char)(code + 32));
        builder.Append((char)(x + 32));
        builder.Append((char)(y + 32));
        return builder.ToString();
    }
}
