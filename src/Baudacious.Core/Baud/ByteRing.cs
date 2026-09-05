namespace Baudacious.Core.Baud;

/// <summary>
/// An unbounded single-producer/single-consumer byte queue built from recycled fixed-size
/// segments. The PTY reader thread writes; the render thread reads.
/// </summary>
/// <remarks>
/// <para>
/// Unbounded is deliberate. specs/baud-governor requires that the hosted shell is never
/// made to block on writing and that its execution time is independent of the selected
/// baud rate, so backpressure is exactly the thing this must not apply. A 10 MB burst at
/// 300 baud is 93 hours of wire time, and it all has to be held.
/// </para>
/// <para>
/// <b>Concurrency.</b> The byte path is lock-free: each segment has an append index only
/// the producer advances and a take index only the consumer advances, both published with
/// volatile writes, so the two threads never contend over data. The lock covers only the
/// segment chain and the recycling pool — entered once per segment, which at 115200 baud
/// is roughly once every 0.7 seconds. Contended-lock cost is therefore not on the hot
/// path, and the render thread cannot be made to wait on a bulk copy.
/// </para>
/// <para>
/// Only one thread may produce and only one may consume. Two producers will corrupt the
/// tail.
/// </para>
/// </remarks>
public sealed class ByteRing
{
    /// <summary>
    /// 8 KB. Large enough that segment turnover is rare even at 115200 baud, small enough
    /// that a mostly-empty queue is not holding megabytes.
    /// </summary>
    public const int DefaultSegmentSize = 8 * 1024;

    /// <summary>Segments kept for reuse. Beyond this they are dropped for the GC.</summary>
    private const int MaxPooledSegments = 64;

    private readonly int _segmentSize;
    private readonly Lock _chain = new();

    private Segment _head;
    private Segment _tail;
    private Segment? _pool;
    private int _pooledCount;
    private long _count;

    public ByteRing(int segmentSize = DefaultSegmentSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(segmentSize, 64);
        _segmentSize = segmentSize;
        _head = _tail = new Segment(segmentSize);
    }

    /// <summary>Bytes written but not yet taken.</summary>
    public long Count => Interlocked.Read(ref _count);

    public bool IsEmpty => Count == 0;

    /// <summary>Appends bytes. Producer thread only. Never blocks and never drops.</summary>
    public void Write(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return;
        }

        var written = 0;
        while (!bytes.IsEmpty)
        {
            var tail = _tail;
            var offset = tail.Append;
            var space = tail.Buffer.Length - offset;

            if (space == 0)
            {
                var next = RentSegment();

                // Publish the successor before moving the tail: the consumer follows Next
                // to decide a segment is finished, and must never see a stale null after
                // the producer has moved on.
                Volatile.Write(ref tail.Next, next);
                _tail = next;
                continue;
            }

            var n = Math.Min(space, bytes.Length);
            bytes[..n].CopyTo(tail.Buffer.AsSpan(offset));

            // Publishing the index is what makes the bytes visible; the copy above must
            // not be reordered past it.
            Volatile.Write(ref tail.Append, offset + n);

            bytes = bytes[n..];
            written += n;
        }

        Interlocked.Add(ref _count, written);
    }

    /// <summary>
    /// Takes up to <paramref name="destination"/>.Length bytes. Consumer thread only.
    /// Returns 0 when nothing is available; never blocks.
    /// </summary>
    public int Read(Span<byte> destination)
    {
        var taken = 0;
        while (taken < destination.Length)
        {
            var head = _head;
            var append = Volatile.Read(ref head.Append);
            var available = append - head.Take;

            if (available == 0)
            {
                // Nothing left in this segment. If the producer has moved on, this one is
                // finished and can be recycled; otherwise the queue is genuinely drained.
                if (Volatile.Read(ref head.Next) is not { } next)
                {
                    break;
                }

                _head = next;
                ReturnSegment(head);
                continue;
            }

            var n = Math.Min(available, destination.Length - taken);
            head.Buffer.AsSpan(head.Take, n).CopyTo(destination[taken..]);
            Volatile.Write(ref head.Take, head.Take + n);
            taken += n;
        }

        if (taken > 0)
        {
            Interlocked.Add(ref _count, -taken);
        }

        return taken;
    }

    /// <summary>
    /// Discards everything currently queued without copying it. Consumer thread only.
    /// Returns how many bytes went. Bytes the producer adds during the call may survive,
    /// which is correct: this discards the backlog as it stood, not future arrivals.
    /// </summary>
    public long DiscardAll()
    {
        long discarded = 0;
        while (true)
        {
            var head = _head;
            var append = Volatile.Read(ref head.Append);
            var available = append - head.Take;

            if (available > 0)
            {
                Volatile.Write(ref head.Take, append);
                discarded += available;
                continue;
            }

            if (Volatile.Read(ref head.Next) is not { } next)
            {
                break;
            }

            _head = next;
            ReturnSegment(head);
        }

        if (discarded > 0)
        {
            Interlocked.Add(ref _count, -discarded);
        }

        return discarded;
    }

    private Segment RentSegment()
    {
        lock (_chain)
        {
            if (_pool is { } segment)
            {
                _pool = segment.NextFree;
                _pooledCount--;
                segment.Reset();
                return segment;
            }
        }

        return new Segment(_segmentSize);
    }

    private void ReturnSegment(Segment segment)
    {
        lock (_chain)
        {
            if (_pooledCount >= MaxPooledSegments)
            {
                return;
            }

            segment.NextFree = _pool;
            _pool = segment;
            _pooledCount++;
        }
    }

    private sealed class Segment(int size)
    {
        public readonly byte[] Buffer = new byte[size];

        /// <summary>Write cursor. Producer advances; consumer reads volatile.</summary>
        public int Append;

        /// <summary>Read cursor. Consumer advances; producer never reads it.</summary>
        public int Take;

        /// <summary>Successor in the queue. Producer publishes; consumer reads volatile.</summary>
        public Segment? Next;

        /// <summary>Successor in the recycling pool. Only touched under the chain lock.</summary>
        public Segment? NextFree;

        public void Reset()
        {
            Append = 0;
            Take = 0;
            Next = null;
            NextFree = null;
        }
    }
}
