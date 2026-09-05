using System.Text;
using Baudacious.Pty;

namespace Baudacious.ConPtySpike;

/// <summary>Prints arriving bytes with control characters made visible.</summary>
internal sealed class ConsoleSink : IPtyOutputSink
{
    private readonly bool _escapeVisible;
    private long _bytes;

    public ConsoleSink(bool escapeVisible) => _escapeVisible = escapeVisible;

    public long Bytes => Volatile.Read(ref _bytes);

    public int Chunks { get; private set; }

    public bool Closed { get; private set; }

    private readonly long _start = System.Diagnostics.Stopwatch.GetTimestamp();

    public void OnBytes(ReadOnlySpan<byte> bytes, long timestamp)
    {
        Interlocked.Add(ref _bytes, bytes.Length);
        Chunks++;
        var ms = (timestamp - _start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        Console.WriteLine($"  [{ms,8:F1} ms] {bytes.Length,5} bytes  {Vt.Visible(bytes).ReplaceLineEndings("\\n")}");
        _ = _escapeVisible;
        _ = Encoding.UTF8.GetString([]);
    }

    public void OnClosed()
    {
        Closed = true;
        Console.WriteLine();
        Console.WriteLine($"[sink] end of stream after {Bytes} bytes in {Chunks} chunks");
    }
}
