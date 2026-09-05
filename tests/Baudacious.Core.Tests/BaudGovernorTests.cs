using Baudacious.Core.Baud;
using Baudacious.Core.Timing;

namespace Baudacious.Core.Tests;

/// <summary>
/// The product's central claim, asserted exactly. Every test here drives a fake clock, so
/// there are no sleeps, nothing is timing-dependent, and the numbers are exact rather than
/// approximate.
/// </summary>
public class BaudGovernorTests
{
    private static (ByteRing Ring, BaudGovernor Governor, FakeTimeSource Clock, CollectingConsumer Sink)
        Build(LineRate rate, int queuedBytes = 1_000_000)
    {
        var ring = new ByteRing();
        var clock = new FakeTimeSource();
        var governor = new BaudGovernor(ring, clock, rate);
        if (queuedBytes > 0)
        {
            ring.Write(Pattern(queuedBytes));
        }

        return (ring, governor, clock, new CollectingConsumer());
    }

    /// <summary>A recognisable, non-repeating-per-256 pattern so ordering errors show up.</summary>
    private static byte[] Pattern(int length)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)(i % 251);
        }

        return bytes;
    }

    // ---- Rate and framing --------------------------------------------------

    [Theory]
    [InlineData(110, 11, 10)]      // ASR-33
    [InlineData(300, 10, 30)]
    [InlineData(1200, 10, 120)]
    [InlineData(2400, 10, 240)]
    [InlineData(9600, 10, 960)]
    [InlineData(115200, 10, 11520)]
    [InlineData(1200, 11, 109)]    // 8N2: 109.09/s, so 109 whole bytes in the first second
    public void OneSecond_releasesExactlyTheRatesWorthOfBytes(int baud, int bitsPerChar, int expected)
    {
        var framing = bitsPerChar == 11 ? Framing.N82 : Framing.N81;
        var (_, governor, clock, sink) = Build(LineRate.Of(baud, framing));

        clock.AdvanceSeconds(1);
        governor.Tick(sink);

        Assert.Equal(expected, sink.Count);
    }

    [Fact]
    public void EightN2_isMeasurablySlowerThanEightN1_atTheSameBaud()
    {
        var (_, fast, fastClock, fastSink) = Build(LineRate.Of(1200, Framing.N81));
        var (_, slow, slowClock, slowSink) = Build(LineRate.Of(1200, Framing.N82));

        fastClock.AdvanceSeconds(10);
        slowClock.AdvanceSeconds(10);
        fast.Tick(fastSink);
        slow.Tick(slowSink);

        Assert.Equal(1200, fastSink.Count);
        Assert.Equal(1090, slowSink.Count);   // 10 s x 109.0909/s
    }

    // ---- Fractional carry, the thing that makes slow rates exact -----------

    [Fact]
    public void ThreeHundredBaud_atSixtyHertz_releasesOnAlternatingFrames_andExactlyThirtyPerSecond()
    {
        // Half a byte per frame. Counting frames, or rounding without carry, gets this wrong.
        var (_, governor, clock, sink) = Build(LineRate.Of(300));

        var releasedPerFrame = new List<int>();
        for (var frame = 0; frame < 60; frame++)
        {
            clock.AdvanceFrame();
            releasedPerFrame.Add(governor.Tick(sink));
        }

        Assert.Equal(30, sink.Count);
        Assert.Equal(30, releasedPerFrame.Count(n => n == 1));
        Assert.Equal(30, releasedPerFrame.Count(n => n == 0));
        Assert.DoesNotContain(releasedPerFrame, n => n > 1);
    }

    [Fact]
    public void OneHundredTenBaud_releasesOneByteEverySixthFrame()
    {
        var (_, governor, clock, sink) = Build(LineRate.Of(110));

        for (var frame = 0; frame < 60; frame++)
        {
            clock.AdvanceFrame();
            governor.Tick(sink);
        }

        Assert.Equal(10, sink.Count);
    }

    [Fact]
    public void NoDrift_overTenThousandFrames()
    {
        // 10,000 frames at 60 Hz is 166.67 s; at 1200 baud 8N1 that is 20,000 bytes.
        var (_, governor, clock, sink) = Build(LineRate.Of(1200), queuedBytes: 4_000_000);

        for (var frame = 0; frame < 10_000; frame++)
        {
            clock.AdvanceFrame();
            governor.Tick(sink);
        }

        var expected = 10_000 / 60.0 * 120.0;
        Assert.InRange(sink.Count, expected - 2, expected + 2);
    }

    [Fact]
    public void IrregularFrameTimes_doNotChangeTheTotal()
    {
        // Jittering the frame interval must not change how many bytes a second buys.
        var (_, governor, clock, sink) = Build(LineRate.Of(9600));
        var jitter = new[] { 3.0, 21.0, 9.0, 40.0, 1.0, 17.0 };

        var elapsed = 0.0;
        var i = 0;
        while (elapsed < 1000.0)
        {
            var step = jitter[i++ % jitter.Length];
            clock.AdvanceMilliseconds(step);
            elapsed += step;
            governor.Tick(sink);
        }

        Assert.Equal((int)(elapsed / 1000.0 * 960), sink.Count);
    }

    // ---- Stalls ------------------------------------------------------------

    [Fact]
    public void AfterAStall_theOwedBytesAreReleased()
    {
        var (_, governor, clock, sink) = Build(LineRate.Of(9600));

        clock.AdvanceFrame();
        governor.Tick(sink);
        var afterFirstFrame = sink.Count;

        // The renderer hangs for 250 ms, then resumes.
        clock.AdvanceMilliseconds(250);
        var caughtUp = governor.Tick(sink);

        Assert.Equal(240, caughtUp);                       // 0.25 s x 960/s
        Assert.Equal(afterFirstFrame + 240, sink.Count);
    }

    [Fact]
    public void AWorkCap_defersBytesButNeverLosesThem()
    {
        var (_, governor, clock, sink) = Build(LineRate.Of(115200), queuedBytes: 200_000);
        governor.ParseBudgetBytes = 1000;

        clock.AdvanceSeconds(10);            // entitles the line to 115,200 bytes
        var first = governor.Tick(sink);
        Assert.Equal(1000, first);

        // No further time passes, but the retained credit must still be spendable.
        var second = governor.Tick(sink);
        Assert.Equal(1000, second);

        var total = first + second;
        for (var i = 0; i < 200; i++)
        {
            total += governor.Tick(sink);
        }

        Assert.Equal(115_200, total);
    }

    // ---- An idle line does not bank wire time ------------------------------

    [Fact]
    public void QuietTime_doesNotAccumulateCredit()
    {
        var ring = new ByteRing();
        var clock = new FakeTimeSource();
        var governor = new BaudGovernor(ring, clock, LineRate.Of(1200));
        var sink = new CollectingConsumer();

        // A minute of silence.
        for (var i = 0; i < 60; i++)
        {
            clock.AdvanceSeconds(1);
            governor.Tick(sink);
        }

        Assert.Equal(0, sink.Count);

        // Now data arrives. It must trickle at 120/s, not arrive as a 7200-byte dump.
        ring.Write(Pattern(10_000));
        clock.AdvanceSeconds(1);
        governor.Tick(sink);

        Assert.Equal(120, sink.Count);
    }

    // ---- Rate changes ------------------------------------------------------

    [Fact]
    public void ChangingRateMidStream_discardsNothingAndAppliesImmediately()
    {
        var (ring, governor, clock, sink) = Build(LineRate.Of(300), queuedBytes: 0);
        ring.Write(Pattern(3000));

        clock.AdvanceSeconds(1);
        governor.Tick(sink);
        Assert.Equal(30, sink.Count);

        governor.Rate = LineRate.Of(9600);
        clock.AdvanceSeconds(1);
        governor.Tick(sink);

        Assert.Equal(30 + 960, sink.Count);
        Assert.Equal(3000 - 990, governor.PendingBytes);
    }

    [Fact]
    public void ChangingFraming_changesSpeedWithoutLosingBytes()
    {
        var (_, governor, clock, sink) = Build(LineRate.Of(1200, Framing.N81));

        clock.AdvanceSeconds(1);
        governor.Tick(sink);
        Assert.Equal(120, sink.Count);

        governor.Rate = LineRate.Of(1200, Framing.N82);
        clock.AdvanceSeconds(1);
        governor.Tick(sink);

        Assert.Equal(120 + 109, sink.Count);
    }

    [Fact]
    public void Unlimited_releasesEverythingImmediately()
    {
        var (_, governor, clock, sink) = Build(LineRate.Unlimited, queuedBytes: 5000);

        clock.AdvanceMilliseconds(1);
        governor.Tick(sink);

        Assert.Equal(5000, sink.Count);
        Assert.Equal(0, governor.PendingBytes);
    }

    // ---- Escape hatches ----------------------------------------------------

    [Fact]
    public void FastForward_releasesTheBacklogAndThenReturnsToRate()
    {
        var (ring, governor, clock, sink) = Build(LineRate.Of(1200), queuedBytes: 50_000);

        governor.FastForward();
        clock.AdvanceFrame();
        governor.Tick(sink);

        Assert.Equal(50_000, sink.Count);
        Assert.Equal(0, governor.PendingBytes);
        Assert.False(governor.IsFastForwarding);

        // Back to normal metering afterwards.
        ring.Write(Pattern(1000));
        clock.AdvanceSeconds(1);
        governor.Tick(sink);
        Assert.Equal(50_000 + 120, sink.Count);
    }

    [Fact]
    public void FastForward_spillsAcrossTicksWhenTheBacklogExceedsTheWorkCap()
    {
        var (_, governor, clock, sink) = Build(LineRate.Of(1200), queuedBytes: 10_000);
        governor.ParseBudgetBytes = 4096;

        governor.FastForward();
        clock.AdvanceFrame();
        Assert.Equal(4096, governor.Tick(sink));
        Assert.True(governor.IsFastForwarding);

        clock.AdvanceFrame();
        Assert.Equal(4096, governor.Tick(sink));

        clock.AdvanceFrame();
        Assert.Equal(10_000 - 8192, governor.Tick(sink));
        Assert.False(governor.IsFastForwarding);
        Assert.Equal(10_000, sink.Count);
    }

    [Fact]
    public void FastForward_producesTheSameBytesInTheSameOrderAsMeteredDelivery()
    {
        // The invariant that keeps the quick and slow routes from disagreeing.
        var payload = Pattern(20_000);

        var slowRing = new ByteRing();
        var slowClock = new FakeTimeSource();
        var slow = new BaudGovernor(slowRing, slowClock, LineRate.Of(9600));
        var slowSink = new CollectingConsumer();
        slowRing.Write(payload);
        while (slow.PendingBytes > 0)
        {
            slowClock.AdvanceFrame();
            slow.Tick(slowSink);
        }

        var fastRing = new ByteRing();
        var fastClock = new FakeTimeSource();
        var fast = new BaudGovernor(fastRing, fastClock, LineRate.Of(9600));
        var fastSink = new CollectingConsumer();
        fastRing.Write(payload);
        fast.FastForward();
        fastClock.AdvanceFrame();
        fast.Tick(fastSink);

        Assert.Equal(payload, slowSink.ToArray());
        Assert.Equal(payload, fastSink.ToArray());
    }

    [Fact]
    public void Flush_discardsTheBacklogAndReleasesNothingFurther()
    {
        var (_, governor, clock, sink) = Build(LineRate.Of(1200), queuedBytes: 500_000);

        clock.AdvanceSeconds(1);
        governor.Tick(sink);
        Assert.Equal(120, sink.Count);

        var discarded = governor.Flush();
        Assert.Equal(500_000 - 120, discarded);
        Assert.Equal(0, governor.PendingBytes);

        clock.AdvanceSeconds(5);
        governor.Tick(sink);
        Assert.Equal(120, sink.Count);
    }

    [Fact]
    public void Flush_thenNewOutput_startsCleanAtTheConfiguredRate()
    {
        var (ring, governor, clock, sink) = Build(LineRate.Of(1200), queuedBytes: 100_000);

        governor.Flush();
        ring.Write(Pattern(1000));
        clock.AdvanceSeconds(1);
        governor.Tick(sink);

        Assert.Equal(120, sink.Count);
    }

    // ---- Held 10x skim -----------------------------------------------------

    [Fact]
    public void SpeedMultiplier_scalesTheRateWhileHeld()
    {
        var (_, governor, clock, sink) = Build(LineRate.Of(1200));

        governor.SpeedMultiplier = 10;
        clock.AdvanceSeconds(2);
        governor.Tick(sink);
        Assert.Equal(2400, sink.Count);

        governor.SpeedMultiplier = 1;
        clock.AdvanceSeconds(1);
        governor.Tick(sink);
        Assert.Equal(2400 + 120, sink.Count);
    }

    // ---- Backlog reporting -------------------------------------------------

    [Fact]
    public void PendingBytesAndDrainEstimate_matchTheSpecWorkedExample()
    {
        var (_, governor, _, _) = Build(LineRate.Of(1200), queuedBytes: 12_000);

        Assert.Equal(12_000, governor.PendingBytes);
        Assert.Equal(100.0, governor.EstimatedDrain.TotalSeconds, 6);
    }

    [Fact]
    public void DrainEstimate_isZeroWhenNothingIsQueued()
    {
        var (_, governor, _, _) = Build(LineRate.Of(1200), queuedBytes: 0);
        Assert.Equal(TimeSpan.Zero, governor.EstimatedDrain);
    }

    [Fact]
    public void DrainEstimate_shrinksWhenTheRateRises()
    {
        var (_, governor, _, _) = Build(LineRate.Of(300), queuedBytes: 3000);
        Assert.Equal(100.0, governor.EstimatedDrain.TotalSeconds, 6);

        governor.Rate = LineRate.Of(9600);
        Assert.Equal(3000 / 960.0, governor.EstimatedDrain.TotalSeconds, 6);
    }

    // ---- Byte fidelity -----------------------------------------------------

    [Fact]
    public void MeteredDelivery_preservesEveryByteInOrder()
    {
        var payload = Pattern(65_536);
        var ring = new ByteRing();
        var clock = new FakeTimeSource();
        var governor = new BaudGovernor(ring, clock, LineRate.Of(38400));
        var sink = new CollectingConsumer();
        ring.Write(payload);

        while (governor.PendingBytes > 0)
        {
            clock.AdvanceFrame();
            governor.Tick(sink);
        }

        Assert.Equal(payload, sink.ToArray());
    }

    [Fact]
    public void BytesWrittenWhileDraining_areAppendedNotInterleaved()
    {
        var ring = new ByteRing();
        var clock = new FakeTimeSource();
        var governor = new BaudGovernor(ring, clock, LineRate.Of(9600));
        var sink = new CollectingConsumer();

        var first = Pattern(5000);
        var second = new byte[5000];
        Array.Fill(second, (byte)0xAA);

        ring.Write(first);
        for (var i = 0; i < 10; i++)
        {
            clock.AdvanceFrame();
            governor.Tick(sink);
        }

        ring.Write(second);
        while (governor.PendingBytes > 0)
        {
            clock.AdvanceFrame();
            governor.Tick(sink);
        }

        Assert.Equal([.. first, .. second], sink.ToArray());
    }
}
