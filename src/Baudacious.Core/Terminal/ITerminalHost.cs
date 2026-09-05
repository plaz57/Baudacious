namespace Baudacious.Core.Terminal;

/// <summary>
/// What the emulator needs from the outside world: a way to answer queries and a place to
/// put the window title.
/// </summary>
public interface ITerminalHost
{
    /// <summary>
    /// Sends bytes back to the hosted shell — a reply to a status or identity query.
    /// </summary>
    /// <remarks>
    /// Answering is not optional. ConPTY issues a cursor-position report during startup
    /// and blocks until the terminal replies, so a terminal that ignores DSR does not
    /// merely look incomplete, it hangs with an empty screen. Phase 0 found this the hard
    /// way.
    /// </remarks>
    void Reply(ReadOnlySpan<byte> bytes);

    void SetTitle(string title);

    void Bell();
}

/// <summary>A host that discards everything, for tests and headless replay.</summary>
public sealed class NullTerminalHost : ITerminalHost
{
    public static readonly NullTerminalHost Instance = new();

    public string? Title { get; private set; }

    public int BellCount { get; private set; }

    public void Reply(ReadOnlySpan<byte> bytes)
    {
    }

    public void SetTitle(string title) => Title = title;

    public void Bell() => BellCount++;
}
