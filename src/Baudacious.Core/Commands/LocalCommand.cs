using Baudacious.Core.Baud;

namespace Baudacious.Core.Commands;

/// <summary>
/// A parsed local command: what the user asked for, with nothing yet done about it.
/// </summary>
/// <remarks>
/// The prompt parses; the window acts. Keeping the two apart is what lets the whole
/// vocabulary be tested without a GPU context, and it means a mistyped command is rejected
/// before anything has been changed.
/// </remarks>
public abstract record LocalCommand
{
    private LocalCommand()
    {
    }

    /// <summary>An empty line. Not an error, and not worth remembering.</summary>
    public sealed record Nothing : LocalCommand;

    public sealed record SetRate(LineRate Rate) : LocalCommand;

    public sealed record SetFraming(Framing Framing) : LocalCommand;

    public sealed record Flush : LocalCommand;

    /// <summary>The palette by name; the window owns the enum it belongs to.</summary>
    public sealed record SetPhosphor(string Name) : LocalCommand;

    public sealed record SetFontSize(int Scale) : LocalCommand;

    /// <summary>Null means toggle, which is what a bare <c>crt</c> asks for.</summary>
    public sealed record SetCrt(bool? On) : LocalCommand;

    /// <summary>Null means toggle, as with <see cref="SetCrt"/>.</summary>
    public sealed record SetFastEcho(bool? On) : LocalCommand;

    public sealed record NewSession(string Profile) : LocalCommand;

    public sealed record ShowHelp : LocalCommand;

    /// <summary>The version, the licence, and whose typeface this is.</summary>
    public sealed record ShowAbout : LocalCommand;

    public sealed record Quit : LocalCommand;

    /// <summary>Nothing happens and the prompt stays open carrying this message.</summary>
    public sealed record Failed(string Message) : LocalCommand;
}
