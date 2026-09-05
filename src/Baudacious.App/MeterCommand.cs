using System.Diagnostics;
using System.Globalization;
using Baudacious.Core.Baud;
using Baudacious.Core.Timing;

namespace Baudacious.App;

/// <summary>
/// Plays a recorded byte stream onto the console at a chosen line rate.
/// </summary>
/// <remarks>
/// The Phase 1 demonstration: no window, no emulator, no shell — just the governor, the
/// real clock, and a capture from Phase 0, arriving at the speed a real line would have
/// delivered it. Phase 3 replaces the console here with the GPU renderer and the file
/// with a live ConPTY session; the governor between them does not change.
/// </remarks>
internal static class MeterCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help")
        {
            Usage();
            return args.Length == 0 ? 1 : 0;
        }

        var path = args[0];
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"no such capture: {path}");
            return 1;
        }

        var rate = LineRate.Of(1200);
        var visible = false;
        var quiet = false;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--baud" or "-b" when i + 1 < args.Length:
                    if (!LineRate.TryParse(args[++i], out rate))
                    {
                        Console.Error.WriteLine($"not a rate: {args[i]} (1-{LineRate.MaxBaud}, or 'unlimited')");
                        return 1;
                    }

                    break;

                case "--framing" or "-f" when i + 1 < args.Length:
                    if (!Framing.TryParse(args[++i], out var framing))
                    {
                        Console.Error.WriteLine($"not a framing: {args[i]} (e.g. 8N1, 7E1, 8N2)");
                        return 1;
                    }

                    rate = rate.IsUnlimited ? rate : LineRate.Of(rate.Baud, framing);
                    break;

                case "--visible" or "-v":
                    visible = true;
                    break;

                case "--quiet" or "-q":
                    quiet = true;
                    break;

                default:
                    Console.Error.WriteLine($"unrecognised option: {args[i]}");
                    Usage();
                    return 1;
            }
        }

        var payload = File.ReadAllBytes(path);

        // Everything is queued up front, exactly as a fast host would have delivered it to
        // a slow line: the producer finishes instantly, and the wait that follows is the
        // wire, not the work.
        var ring = new ByteRing();
        ring.Write(payload);

        var governor = new BaudGovernor(ring, SystemTimeSource.Instance, rate);

        if (!quiet)
        {
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{Path.GetFileName(path)}: {payload.Length} bytes at {rate}"));
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"predicted {Describe(governor.EstimatedDrain)} to drain"));
            Console.Error.WriteLine();
            Console.Error.Flush();
        }

        using var stdout = Console.OpenStandardOutput();
        IByteConsumer sink = visible ? new VisibleConsumer(stdout) : new RawConsumer(stdout);

        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            governor.Tick(sink);
            if (governor.PendingBytes == 0)
            {
                break;
            }

            // Not pacing — the wall clock does that. This only stops a spin loop from
            // burning a core between frames; the governor is indifferent to how often it
            // is called, which is the point of the credit accumulator.
            //
            // The break above comes before the sleep deliberately. Sleeping after the
            // final byte adds a whole quantum — Thread.Sleep(1) is 1-15 ms on Windows —
            // to the measured time, which is 0.04% of a 31-second run at 300 baud but
            // 1.3% of a one-second run at 9600.
            Thread.Sleep(1);
        }

        stopwatch.Stop();
        stdout.Flush();

        if (quiet)
        {
            return 0;
        }

        var elapsed = stopwatch.Elapsed.TotalSeconds;
        var expected = rate.IsUnlimited ? 0 : payload.Length / rate.BytesPerSecond;

        Console.Error.WriteLine();
        Console.Error.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"released {governor.TotalReleased} of {payload.Length} bytes in {elapsed:F3} s"));

        if (!rate.IsUnlimited)
        {
            var error = (elapsed - expected) / expected * 100.0;
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"expected {expected:F3} s at {rate.BytesPerSecond:F2} bytes/s -> error {error:+0.000;-0.000}%"));
        }

        return governor.TotalReleased == payload.Length ? 0 : 1;
    }

    private static string Describe(TimeSpan span) =>
        span.TotalSeconds >= 60
            ? string.Create(CultureInfo.InvariantCulture, $"{span.TotalMinutes:F1} min")
            : string.Create(CultureInfo.InvariantCulture, $"{span.TotalSeconds:F2} s");

    private static void Usage() =>
        Console.Error.WriteLine("""
            usage: Baudacious.App meter <capture.bin> [options]

              -b, --baud <rate>      line rate, or 'unlimited' (default 1200)
              -f, --framing <spec>   8N1 (default), 7E1, 8N2, 8E1
              -v, --visible          show control bytes instead of acting on them
              -q, --quiet            stream only, no summary

            Plays a recorded byte stream at the given rate. Capture files live in
            tests/fixtures/conpty-captures/.
            """);

    /// <summary>Writes bytes through untouched, so the console acts on the escape sequences.</summary>
    private sealed class RawConsumer(Stream output) : IByteConsumer
    {
        public void Consume(ReadOnlySpan<byte> bytes)
        {
            output.Write(bytes);
            output.Flush();
        }
    }

    /// <summary>Renders control bytes as visible tokens, for watching what is on the wire.</summary>
    private sealed class VisibleConsumer(Stream output) : IByteConsumer
    {
        private readonly byte[] _scratch = new byte[64];

        public void Consume(ReadOnlySpan<byte> bytes)
        {
            foreach (var b in bytes)
            {
                var text = b switch
                {
                    0x1B => "<ESC>",
                    0x07 => "<BEL>",
                    0x08 => "<BS>",
                    0x09 => "<TAB>",
                    0x0A => "<LF>\n",
                    0x0D => "<CR>",
                    < 0x20 or 0x7F => null,
                    _ => null,
                };

                if (text is not null)
                {
                    Write(text);
                }
                else if (b is >= 0x20 and < 0x7F)
                {
                    _scratch[0] = b;
                    output.Write(_scratch.AsSpan(0, 1));
                }
                else
                {
                    Write($"<{b:X2}>");
                }
            }

            output.Flush();
        }

        private void Write(string text)
        {
            var n = System.Text.Encoding.ASCII.GetBytes(text, _scratch);
            output.Write(_scratch.AsSpan(0, n));
        }
    }
}
