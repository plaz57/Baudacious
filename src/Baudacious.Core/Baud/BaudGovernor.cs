using Baudacious.Core.Timing;

namespace Baudacious.Core.Baud;

/// <summary>
/// Releases queued bytes onto the display at the configured line rate. This is the
/// mechanism the entire product exists to provide.
/// </summary>
/// <remarks>
/// <para>
/// It meters <b>raw bytes</b>, ahead of any escape-sequence interpretation: a UART has no
/// idea what ANSI is, so <c>ESC[31m</c> costs five characters of wire time exactly as
/// five letters would. Phase 0 measured escape sequences at 28–48% of the byte stream in
/// interactive use, so metering decoded glyphs instead would deliver a session at roughly
/// twice the speed a real line ever did.
/// </para>
/// <para>
/// <b>Timing model.</b> Each tick converts elapsed wall-clock time into a byte budget and
/// carries the fraction over. Time is never slept away and frames are never counted: at
/// 300 baud a 60 Hz frame earns half a byte, so counting frames would be wrong by
/// construction and any rounding without carry would drift. A dropped frame is therefore
/// self-correcting — the next tick simply sees a larger elapsed time and owes more bytes.
/// </para>
/// </remarks>
public sealed class BaudGovernor
{
    /// <summary>
    /// Ceiling on bytes interpreted in a single tick. At 115200 baud a tick normally
    /// releases 192 bytes, so this engages only when catching up after a stall or when
    /// fast-forwarding — where it stops one enormous burst from blowing the frame budget.
    /// Unspent credit is retained, so capping the work never loses wire time.
    /// </summary>
    public const int DefaultParseBudgetBytes = 256 * 1024;

    private const int ScratchSize = 64 * 1024;

    /// <summary>
    /// Credit is never allowed past ten million bytes' worth. Only reachable if the work
    /// cap is set absurdly low; it keeps the fixed-point numerator far from overflowing.
    /// </summary>
    private const long MaxCreditBytes = 10_000_000;

    private readonly ByteRing _input;
    private readonly ITimeSource _time;
    private readonly byte[] _scratch = new byte[ScratchSize];

    private LineRate _rate;
    private long _lastTimestamp;
    private int _speedMultiplier = 1;

    /// <summary>
    /// Accrued credit, held as an exact fraction: <c>_creditUnits / _creditDenominator</c>
    /// bytes, where the denominator is <c>bitsPerCharacter x clock frequency</c>.
    /// </summary>
    /// <remarks>
    /// Integer, not floating point, and that is not fastidiousness. Accumulating credit as
    /// a double loses the last byte of every second to a 1e-13 shortfall — 300 baud
    /// delivers 29 bytes per second instead of 30 — and the error compounds over a long
    /// session. Exact arithmetic is what lets the tests assert equality rather than
    /// tolerance.
    /// </remarks>
    private long _creditUnits;
    private long _creditDenominator;

    private bool _fastForwarding;

    public BaudGovernor(ByteRing input, ITimeSource time, LineRate rate)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(time);

        _input = input;
        _time = time;
        _lastTimestamp = time.Timestamp;
        _rate = rate;
        _creditDenominator = DenominatorFor(rate);
    }

    /// <summary>
    /// The line rate. Changing it takes effect on the next tick and disturbs nothing else:
    /// queued bytes are neither discarded nor reordered, accrued credit is kept, and the
    /// hosted shell is not signalled.
    /// </summary>
    public LineRate Rate
    {
        get => _rate;
        set
        {
            var denominator = DenominatorFor(value);

            // Credit is a count of bytes, but it is stored over a framing-dependent
            // denominator, so a framing change has to rescale it or the same credit would
            // silently mean a different number of bytes.
            if (_creditUnits != 0 && denominator != _creditDenominator && _creditDenominator > 0)
            {
                _creditUnits = (long)((Int128)_creditUnits * denominator / _creditDenominator);
            }

            _rate = value;
            _creditDenominator = denominator;
        }
    }

    /// <inheritdoc cref="DefaultParseBudgetBytes"/>
    public int ParseBudgetBytes { get; set; } = DefaultParseBudgetBytes;

    /// <summary>
    /// Multiplies the release rate while held. Drives the 10x skim control; 1 is normal.
    /// </summary>
    /// <remarks>Integer, so that scaling the rate keeps the credit arithmetic exact.</remarks>
    public int SpeedMultiplier
    {
        get => _speedMultiplier;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            _speedMultiplier = value;
        }
    }

    private long DenominatorFor(LineRate rate) =>
        (long)rate.Framing.BitsPerCharacter * _time.Frequency;

    /// <summary>Bytes queued but not yet released.</summary>
    public long PendingBytes => _input.Count;

    /// <summary>Whether a fast-forward is still working through the backlog.</summary>
    public bool IsFastForwarding => _fastForwarding;

    /// <summary>Total bytes released since construction.</summary>
    public long TotalReleased { get; private set; }

    /// <summary>
    /// How long the current backlog will take to drain at the current rate — what the
    /// on-screen pending indicator reports.
    /// </summary>
    public TimeSpan EstimatedDrain
    {
        get
        {
            var pending = PendingBytes;
            if (pending == 0 || _rate.IsUnlimited || _fastForwarding)
            {
                return TimeSpan.Zero;
            }

            var perSecond = _rate.BytesPerSecond * _speedMultiplier;
            return perSecond <= 0 ? TimeSpan.MaxValue : TimeSpan.FromSeconds(pending / perSecond);
        }
    }

    /// <summary>
    /// Releases whatever this moment in time entitles the line to, handing it to
    /// <paramref name="consumer"/>. Returns the number of bytes released.
    /// </summary>
    public int Tick(IByteConsumer consumer)
    {
        ArgumentNullException.ThrowIfNull(consumer);

        var now = _time.Timestamp;
        var elapsedTicks = now - _lastTimestamp;
        _lastTimestamp = now;

        // A clock that went backwards means someone handed us a non-monotonic source.
        // Treat it as no time passing rather than owing a negative debt.
        if (elapsedTicks < 0)
        {
            elapsedTicks = 0;
        }

        var metered = !_rate.IsUnlimited && !_fastForwarding;

        int allowance;
        if (metered)
        {
            // Exact: credit gains elapsedTicks x baud x multiplier units, where one byte
            // costs bitsPerCharacter x frequency units.
            _creditUnits += elapsedTicks * _rate.Baud * _speedMultiplier;

            var ceiling = MaxCreditBytes * _creditDenominator;
            if (_creditUnits > ceiling)
            {
                _creditUnits = ceiling;
            }

            allowance = (int)Math.Min(_creditUnits / _creditDenominator, ParseBudgetBytes);
        }
        else
        {
            allowance = ParseBudgetBytes;
        }

        var released = allowance <= 0 ? 0 : Release(consumer, allowance);

        if (metered)
        {
            // Spend only what was actually sent, so a work cap defers bytes rather than
            // losing them.
            _creditUnits -= released * _creditDenominator;

            // An idle line does not bank wire time. Without this, a quiet minute would
            // buy enough credit to dump the next burst instantly — the opposite of the
            // behaviour being reproduced.
            if (_input.IsEmpty)
            {
                _creditUnits = 0;
            }
        }

        if (_fastForwarding && _input.IsEmpty)
        {
            _fastForwarding = false;
        }

        TotalReleased += released;
        return released;
    }

    private int Release(IByteConsumer consumer, int allowance)
    {
        var released = 0;
        while (released < allowance)
        {
            var want = Math.Min(allowance - released, _scratch.Length);
            var got = _input.Read(_scratch.AsSpan(0, want));
            if (got == 0)
            {
                break;
            }

            consumer.Consume(_scratch.AsSpan(0, got));
            released += got;
        }

        return released;
    }

    /// <summary>
    /// Releases the entire backlog as fast as the work cap allows, spilling into later
    /// ticks if it is enormous. Sends nothing to the shell.
    /// </summary>
    /// <remarks>
    /// This deliberately runs through the same <see cref="Tick"/> path and the same
    /// consumer as normal metering. A separate fast path is how the quick and slow routes
    /// end up disagreeing about the final screen state.
    /// </remarks>
    public void FastForward() => _fastForwarding = true;

    /// <summary>
    /// Discards the queued backlog. Returns how many bytes were dropped.
    /// </summary>
    /// <remarks>
    /// Note the asymmetry with <see cref="FastForward"/>: nothing is lost by
    /// fast-forwarding, so the parser must keep its state, whereas discarding can cut an
    /// escape sequence in half and the caller must therefore reset the parser to ground.
    /// The governor cannot do that itself — it does not know what a sequence is, which is
    /// the whole point of metering below the parser.
    /// </remarks>
    public long Flush()
    {
        var discarded = _input.DiscardAll();
        _creditUnits = 0;
        _fastForwarding = false;
        return discarded;
    }
}
