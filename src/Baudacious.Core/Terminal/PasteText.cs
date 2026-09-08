using System.Text;

namespace Baudacious.Core.Terminal;

/// <summary>
/// What pasted text is allowed to carry before it is sent to the shell.
/// </summary>
/// <remarks>
/// <para>
/// A paste is delivered as if typed, so anything in it that the shell would act on as a
/// control code or an escape sequence gets acted on. Text copied from a web page or a
/// file can carry exactly that, and bracketed paste makes it worse rather than better:
/// the framing tells the shell "this is inert", and a payload containing its own closing
/// marker <c>ESC [ 201 ~</c> ends the frame early, after which the rest arrives as
/// keystrokes the shell trusts.
/// </para>
/// <para>
/// So every control character is dropped, except the two a paste legitimately carries:
/// TAB, and CR as the line separator. Dropping ESC removes every escape sequence at
/// once, the closing marker included, without having to recognise any of them. This is
/// what xterm and kitty do, and it applies whether or not bracketed paste is on —
/// unbracketed, the shell's line editor would interpret the same bytes directly.
/// </para>
/// </remarks>
public static class PasteText
{
    /// <summary>
    /// Normalises line endings to CR, which is what a terminal sends for Enter, and
    /// removes every other control character.
    /// </summary>
    public static string Sanitize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // CR, LF, CRLF, NEL, LS and PS all become the one line separator a shell reads.
        var normalised = text.ReplaceLineEndings("\r");

        StringBuilder? builder = null;
        for (var i = 0; i < normalised.Length; i++)
        {
            var c = normalised[i];
            if (IsControl(c))
            {
                // Allocate only once something has to go; the common paste is clean.
                builder ??= new StringBuilder(normalised, 0, i, normalised.Length);
                continue;
            }

            builder?.Append(c);
        }

        return builder?.ToString() ?? normalised;
    }

    /// <summary>
    /// C0 controls other than TAB and CR, DEL, and the C1 range. C1 is U+0080 to U+009F,
    /// control characters in Unicode too, and what some line editors read as 8-bit escape
    /// introducers.
    /// </summary>
    public static bool IsControl(char c) =>
        (c < 0x20 && c is not ('\t' or '\r')) || c is >= (char)0x7F and <= (char)0x9F;
}
