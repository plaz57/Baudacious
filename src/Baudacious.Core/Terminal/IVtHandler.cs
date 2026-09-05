namespace Baudacious.Core.Terminal;

/// <summary>
/// What <see cref="VtParser"/> hands out. One call per recognised unit of the stream.
/// </summary>
public interface IVtHandler
{
    /// <summary>A printable character, already decoded from UTF-8.</summary>
    void Print(uint codepoint);

    /// <summary>A C0 control byte: CR, LF, BS, TAB, BEL and friends.</summary>
    void Execute(byte control);

    /// <summary>
    /// A CSI sequence, e.g. <c>ESC [ 1 ; 31 m</c>.
    /// </summary>
    /// <param name="final">The final byte, 0x40-0x7E.</param>
    /// <param name="parameters">Numeric parameters; an omitted parameter arrives as -1.</param>
    /// <param name="colonMask">Bit <c>n</c> set when parameter <c>n</c> followed a colon rather than a semicolon.</param>
    /// <param name="intermediates">Intermediate bytes, 0x20-0x2F.</param>
    /// <param name="isPrivate">True when the sequence carried a private marker such as <c>?</c>.</param>
    void CsiDispatch(
        char final,
        ReadOnlySpan<int> parameters,
        uint colonMask,
        ReadOnlySpan<char> intermediates,
        bool isPrivate);

    /// <summary>A two-or-three byte escape sequence, e.g. <c>ESC 7</c> or <c>ESC ( B</c>.</summary>
    void EscDispatch(char final, ReadOnlySpan<char> intermediates);

    /// <summary>An OSC string, without its introducer or terminator, e.g. <c>0;window title</c>.</summary>
    void OscDispatch(ReadOnlySpan<char> data);
}
