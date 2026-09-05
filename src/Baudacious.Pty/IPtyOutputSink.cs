namespace Baudacious.Pty;

/// <summary>
/// Receives bytes as they arrive from the pseudoconsole, on the reader thread.
/// </summary>
/// <remarks>
/// Implementations must not block: the reader thread is the only thing draining the
/// ConPTY output pipe, and ConPTY's shutdown blocks until that pipe is drained. A
/// slow sink turns into a shutdown deadlock. Copy the span and return.
/// <para>
/// In the finished product this is implemented by the governor's ring buffer
/// (design.md D8); in the Phase 0 spike it is implemented by the capture recorder.
/// </para>
/// </remarks>
public interface IPtyOutputSink
{
    /// <param name="bytes">The bytes just read. Only valid for the duration of the call.</param>
    /// <param name="timestamp"><see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> taken the instant the read returned.</param>
    void OnBytes(ReadOnlySpan<byte> bytes, long timestamp);

    /// <summary>Called once, on the reader thread, when the output pipe reaches end of stream.</summary>
    void OnClosed();
}
