using System.Diagnostics;
using System.Globalization;
using Baudacious.Core.Baud;
using Baudacious.Core.Timing;
using Xunit.Abstractions;

namespace Baudacious.Core.Tests;

/// <summary>
/// The accuracy requirement from specs/baud-governor, measured against the real clock
/// rather than a fake one. Everything else in the suite proves the arithmetic; this proves
/// the arithmetic is wired to a clock that actually ticks.
/// </summary>
/// <remarks>
/// Runs for three seconds by default so the everyday suite stays quick. The spec's
/// worked example is a sixty-second window; set <c>BAUDACIOUS_CONFORMANCE_SECONDS=60</c> to
/// run exactly that. The tolerance asserted is the spec's 0.5% either way, whatever the
/// duration.
/// </remarks>
[Trait("Category", "Timing")]
public class TimingConformanceTests(ITestOutputHelper output)
{
    private static double ConfiguredSeconds =>
        double.TryParse(
            Environment.GetEnvironmentVariable("BAUDACIOUS_CONFORMANCE_SECONDS"),
            CultureInfo.InvariantCulture,
            out var seconds) && seconds >= 1
            ? seconds
            : 3.0;

    [Fact]
    public void SustainedStream_staysWithinHalfAPercentOfNominal()
    {
        const int Baud = 9600;
        var seconds = ConfiguredSeconds;
        var rate = LineRate.Of(Baud);
        var nominal = rate.BytesPerSecond * seconds;

        var ring = new ByteRing();

        // Comfortably more than the run can consume, so the queue is never empty — the
        // condition the spec's accuracy requirement is stated under.
        ring.Write(new byte[(int)(nominal * 4) + 64 * 1024]);

        var governor = new BaudGovernor(ring, SystemTimeSource.Instance, rate);
        var sink = new CountingConsumer();

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed.TotalSeconds < seconds)
        {
            governor.Tick(sink);
            Thread.Sleep(1);
        }

        // One last tick before stopping the clock. The loop exits on the stopwatch, so
        // without this the final sleep interval — up to 15 ms, since Thread.Sleep(1) is
        // only approximately a millisecond on Windows — counts as elapsed time that the
        // governor was never given the chance to release. That alone reads as a
        // systematic 0.5% shortfall, which is the harness's fault and not the line's.
        governor.Tick(sink);
        stopwatch.Stop();

        // Measure against the time that actually elapsed, not the time asked for.
        var expected = rate.BytesPerSecond * stopwatch.Elapsed.TotalSeconds;
        var lower = expected * 0.995;
        var upper = expected * 1.005;
        var error = (sink.Count - expected) / expected * 100.0;

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{Baud} baud {rate.Framing} for {stopwatch.Elapsed.TotalSeconds:F3} s: " +
            $"released {sink.Count}, expected {expected:F1}, error {error:+0.000;-0.000}%"));

        Assert.InRange(sink.Count, lower, upper);
    }

    [Fact]
    public void SlowRate_deliversItsExactCharacterCountAgainstTheRealClock()
    {
        // 300 baud is half a byte per 60 Hz frame — the case where a frame-counting
        // implementation would be wrong by construction.
        var seconds = Math.Min(ConfiguredSeconds, 5.0);
        var rate = LineRate.Of(300);

        var ring = new ByteRing();
        ring.Write(new byte[4096]);

        var governor = new BaudGovernor(ring, SystemTimeSource.Instance, rate);
        var sink = new CountingConsumer();

        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed.TotalSeconds < seconds)
        {
            governor.Tick(sink);
            Thread.Sleep(1);
        }

        governor.Tick(sink);
        stopwatch.Stop();

        var expected = rate.BytesPerSecond * stopwatch.Elapsed.TotalSeconds;
        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"300 baud for {stopwatch.Elapsed.TotalSeconds:F3} s: released {sink.Count}, expected {expected:F1}"));

        // At 30 bytes/second a single byte is 3.3% of a second's worth, so the tolerance
        // is stated in bytes rather than as a percentage.
        Assert.InRange(sink.Count, expected - 1.5, expected + 1.5);
    }

    private sealed class CountingConsumer : IByteConsumer
    {
        public long Count { get; private set; }

        public void Consume(ReadOnlySpan<byte> bytes) => Count += bytes.Length;
    }
}
