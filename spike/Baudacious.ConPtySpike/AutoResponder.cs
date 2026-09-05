using System.Text;
using Baudacious.Pty;

namespace Baudacious.ConPtySpike;

/// <summary>
/// Answers the terminal queries ConPTY expects a real terminal to answer, and forwards
/// everything to an inner sink.
/// </summary>
/// <remarks>
/// <para><b>This is not optional.</b> ConPTY emits a Device Status Report
/// (<c>ESC[6n</c>, "report cursor position") during startup and will not produce any
/// further output until the terminal replies with <c>ESC[row;colR</c>. A terminal that
/// ignores DSR appears to hang with an empty screen — which is exactly what the first
/// run of this spike did.</para>
/// <para>
/// In the finished product this belongs in the emulator, driven by the real cursor
/// position. It is worth noting the interaction with the baud governor: the query
/// arrives metered like everything else, so at 300 baud the four bytes of
/// <c>ESC[6n</c> take 133 ms to surface before Baudacious can even begin to answer.
/// </para>
/// </remarks>
internal sealed class AutoResponder : IPtyOutputSink
{
    private readonly IPtyOutputSink _inner;
    private readonly List<byte> _pending = new(8);

    private PtySession? _session;

    public AutoResponder(IPtyOutputSink inner) => _inner = inner;

    public int DsrCount { get; private set; }

    public int DaCount { get; private set; }

    public void Attach(PtySession session) => _session = session;

    public void OnBytes(ReadOnlySpan<byte> bytes, long timestamp)
    {
        _inner.OnBytes(bytes, timestamp);
        Scan(bytes);
    }

    public void OnClosed() => _inner.OnClosed();

    /// <summary>
    /// Looks for queries that need an answer. Deliberately minimal: it only tracks
    /// enough state to recognise a CSI sequence that may span two reads.
    /// </summary>
    private void Scan(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            if (_pending.Count == 0)
            {
                if (b == 0x1B)
                {
                    _pending.Add(b);
                }

                continue;
            }

            _pending.Add(b);

            if (_pending.Count == 2)
            {
                if (b != '[')
                {
                    _pending.Clear();
                }

                continue;
            }

            // Still in parameters or intermediates.
            if (b is >= 0x20 and <= 0x3F)
            {
                if (_pending.Count > 32)
                {
                    _pending.Clear();
                }

                continue;
            }

            Respond(CollectionsMarshalSpan());
            _pending.Clear();
        }
    }

    private ReadOnlySpan<byte> CollectionsMarshalSpan() =>
        System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_pending);

    private void Respond(ReadOnlySpan<byte> sequence)
    {
        var final = sequence[^1];
        var parameters = Encoding.ASCII.GetString(sequence[2..^1]);

        switch (final)
        {
            // DSR — Device Status Report.
            case (byte)'n' when parameters == "6":
                // Cursor position. Phase 0 does not track a grid, so it always claims
                // home; ConPTY only needs a well-formed answer to proceed.
                DsrCount++;
                Send("\u001b[1;1R");
                break;

            case (byte)'n' when parameters == "5":
                DsrCount++;
                Send("\u001b[0n"); // "terminal OK"
                break;

            // DA — Device Attributes. Claim a VT100 with an advanced video option,
            // which is what xterm-compatible hosts expect to see.
            case (byte)'c' when parameters.Length == 0 || parameters == "0":
                DaCount++;
                Send("\u001b[?1;2c");
                break;

            default:
                break;
        }
    }

    private void Send(string response)
    {
        // Runs on the reader thread. Writing input is a different pipe, so this cannot
        // deadlock against our own draining of the output pipe.
        _session?.Write(Encoding.ASCII.GetBytes(response));
    }
}
