namespace Baudacious.Core.Timing;

/// <summary>
/// A clock that only moves when told to, so timing behaviour can be asserted exactly.
/// </summary>
/// <remarks>
/// This is the payoff of taking <see cref="ITimeSource"/> as a dependency: a test can
/// say "advance one second at 1200 baud 8N1" and assert on exactly 120 bytes, instead
/// of sleeping and hoping. It lives in the product assembly rather than the test
/// project because the Phase 1 harness uses it too, to replay a capture as fast as the
/// machine allows while still exercising the real timing arithmetic.
/// </remarks>
public sealed class FakeTimeSource : ITimeSource
{
    /// <summary>An arbitrary non-zero origin, so tests cannot accidentally depend on starting at 0.</summary>
    private const long Origin = 1_000_000;

    public FakeTimeSource(long frequency = 10_000_000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(frequency, 1);
        Frequency = frequency;
        Timestamp = Origin;
    }

    public long Timestamp { get; private set; }

    public long Frequency { get; }

    public void Advance(TimeSpan amount) => AdvanceSeconds(amount.TotalSeconds);

    public void AdvanceMilliseconds(double milliseconds) => AdvanceSeconds(milliseconds / 1000.0);

    public void AdvanceSeconds(double seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        AdvanceTicks((long)Math.Round(seconds * Frequency));
    }

    public void AdvanceTicks(long ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        Timestamp += ticks;
    }

    /// <summary>
    /// Advances by exactly one frame of a display running at <paramref name="hertz"/>.
    /// </summary>
    /// <remarks>
    /// A 60 Hz frame is 166666 ticks and two thirds at the default frequency, so the
    /// remainder is carried between calls — as an exact integer fraction, not a double.
    /// Sixty calls then advance the clock by precisely one second. Floating-point carry
    /// lands a few ticks short of a second, which is enough for the governor to release
    /// 29 bytes instead of 30 at 300 baud, and a drift test cannot then tell the clock's
    /// rounding apart from the governor's.
    /// </remarks>
    public void AdvanceFrame(int hertz = 60)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hertz);

        var whole = Math.DivRem(Frequency, hertz, out var remainder);
        _frameRemainder += remainder;
        if (_frameRemainder >= hertz)
        {
            _frameRemainder -= hertz;
            whole++;
        }

        AdvanceTicks(whole);
    }

    private long _frameRemainder;
}
