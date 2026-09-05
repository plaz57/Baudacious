using System.Diagnostics;
using Baudacious.Pty;

namespace Baudacious.ConPtySpike;

/// <summary>
/// Records everything ConPTY emits, with arrival timings, in the two-file shape used
/// by <c>script</c>/<c>scriptreplay</c>:
/// <list type="bullet">
/// <item><c>&lt;name&gt;.bin</c> — the raw byte stream, exactly as received. This is
/// what the Phase 2 parser tests replay.</item>
/// <item><c>&lt;name&gt;.timing</c> — text records of <c>micros offset length</c>
/// indexing into the .bin, so arrival cadence can be analysed without disturbing the
/// byte stream itself.</item>
/// </list>
/// </summary>
/// <remarks>
/// The sink runs on the reader thread and must not block (see <see cref="IPtyOutputSink"/>),
/// so writes go to buffered streams and nothing is formatted or analysed here.
/// </remarks>
internal sealed class CaptureRecorder : IPtyOutputSink, IDisposable
{
    private readonly FileStream _bin;
    private readonly StreamWriter _timing;
    private readonly long _startTimestamp;
    private readonly Lock _gate = new();

    private long _offset;
    private bool _closed;

    public CaptureRecorder(string basePath, string description)
    {
        BinPath = basePath + ".bin";
        TimingPath = basePath + ".timing";

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(BinPath))!);

        _bin = new FileStream(BinPath, FileMode.Create, FileAccess.Write, FileShare.Read, 64 * 1024);
        _timing = new StreamWriter(TimingPath) { AutoFlush = false };
        _startTimestamp = Stopwatch.GetTimestamp();

        _timing.WriteLine($"# baudacious conpty capture");
        _timing.WriteLine($"# {description}");
        _timing.WriteLine($"# recorded {DateTimeOffset.Now:O}");
        _timing.WriteLine($"# columns: micros_since_start offset length");
    }

    public string BinPath { get; }

    public string TimingPath { get; }

    public long TotalBytes => Volatile.Read(ref _offset);

    public int ChunkCount { get; private set; }

    /// <summary>Environment.TickCount64 when the last chunk arrived; used to detect quiescence.</summary>
    public long LastArrivalTick { get; private set; }

    /// <summary>Bytes received since the last <see cref="MarkAndCount"/>, for per-scenario measurement.</summary>
    private long _markOffset;

    public void OnBytes(ReadOnlySpan<byte> bytes, long timestamp)
    {
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            var micros = (timestamp - _startTimestamp) * 1_000_000 / Stopwatch.Frequency;
            _bin.Write(bytes);
            _timing.Write(micros);
            _timing.Write(' ');
            _timing.Write(_offset);
            _timing.Write(' ');
            _timing.Write(bytes.Length);
            _timing.Write('\n');

            _offset += bytes.Length;
            ChunkCount++;
            LastArrivalTick = Environment.TickCount64;
        }
    }

    public void OnClosed()
    {
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            _timing.WriteLine($"# eof after {_offset} bytes in {ChunkCount} chunks");
            _timing.Flush();
            _bin.Flush();
        }
    }

    /// <summary>Returns bytes received since the previous mark, and resets the mark.</summary>
    public long MarkAndCount()
    {
        lock (_gate)
        {
            var delta = _offset - _markOffset;
            _markOffset = _offset;
            return delta;
        }
    }

    /// <summary>Notes a scenario boundary in the timing file so the dump can be segmented.</summary>
    public void Annotate(string note)
    {
        lock (_gate)
        {
            var micros = (Stopwatch.GetTimestamp() - _startTimestamp) * 1_000_000 / Stopwatch.Frequency;
            _timing.Write("# @");
            _timing.Write(micros);
            _timing.Write(" offset=");
            _timing.Write(_offset);
            _timing.Write(' ');
            _timing.Write(note);
            _timing.Write('\n');
        }
    }

    public void Dispose()
    {
        OnClosed();
        _timing.Dispose();
        _bin.Dispose();
    }
}
