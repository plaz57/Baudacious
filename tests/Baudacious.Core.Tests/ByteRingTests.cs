using Baudacious.Core.Baud;

namespace Baudacious.Core.Tests;

public class ByteRingTests
{
    private static byte[] Pattern(int length, int seed = 0)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)((i + seed) % 251);
        }

        return bytes;
    }

    [Fact]
    public void EmptyRing_readsNothing()
    {
        var ring = new ByteRing();
        Assert.True(ring.IsEmpty);
        Assert.Equal(0, ring.Read(new byte[16]));
    }

    [Fact]
    public void WrittenBytes_comeBackInOrder()
    {
        var ring = new ByteRing();
        var payload = Pattern(1000);
        ring.Write(payload);

        var buffer = new byte[1000];
        Assert.Equal(1000, ring.Read(buffer));
        Assert.Equal(payload, buffer);
        Assert.True(ring.IsEmpty);
    }

    [Fact]
    public void WritesSpanningSegmentBoundaries_areSeamless()
    {
        // Small segments so the chain is exercised hard.
        var ring = new ByteRing(segmentSize: 64);
        var payload = Pattern(5000);
        ring.Write(payload);

        Assert.Equal(5000, ring.Count);

        var read = new List<byte>();
        var buffer = new byte[7];   // deliberately coprime with the segment size
        int n;
        while ((n = ring.Read(buffer)) > 0)
        {
            read.AddRange(buffer.AsSpan(0, n));
        }

        Assert.Equal(payload, read.ToArray());
    }

    [Fact]
    public void InterleavedWritesAndReads_preserveOrder()
    {
        var ring = new ByteRing(segmentSize: 128);
        var expected = new List<byte>();
        var actual = new List<byte>();
        var buffer = new byte[100];

        for (var round = 0; round < 50; round++)
        {
            var chunk = Pattern(37, round);
            ring.Write(chunk);
            expected.AddRange(chunk);

            var n = ring.Read(buffer);
            actual.AddRange(buffer.AsSpan(0, n));
        }

        int tail;
        while ((tail = ring.Read(buffer)) > 0)
        {
            actual.AddRange(buffer.AsSpan(0, tail));
        }

        Assert.Equal(expected.ToArray(), actual.ToArray());
    }

    [Fact]
    public void Count_tracksWhatIsOutstanding()
    {
        var ring = new ByteRing(segmentSize: 64);
        ring.Write(Pattern(500));
        Assert.Equal(500, ring.Count);

        ring.Read(new byte[200]);
        Assert.Equal(300, ring.Count);

        ring.Write(Pattern(100));
        Assert.Equal(400, ring.Count);
    }

    [Fact]
    public void DiscardAll_emptiesTheQueueWithoutCopying()
    {
        var ring = new ByteRing(segmentSize: 64);
        ring.Write(Pattern(10_000));

        Assert.Equal(10_000, ring.DiscardAll());
        Assert.True(ring.IsEmpty);
        Assert.Equal(0, ring.Read(new byte[16]));
    }

    [Fact]
    public void AfterDiscard_theRingStillWorks()
    {
        var ring = new ByteRing(segmentSize: 64);
        ring.Write(Pattern(10_000));
        ring.DiscardAll();

        var payload = Pattern(300, seed: 7);
        ring.Write(payload);

        var buffer = new byte[300];
        Assert.Equal(300, ring.Read(buffer));
        Assert.Equal(payload, buffer);
    }

    [Fact]
    public void UnboundedGrowth_holdsALargeBacklogWithoutBlocking()
    {
        // specs/baud-governor: 10 MB queued at 1200 baud must not block the producer.
        var ring = new ByteRing();
        for (var i = 0; i < 10; i++)
        {
            ring.Write(Pattern(1024 * 1024, i));
        }

        Assert.Equal(10L * 1024 * 1024, ring.Count);
    }

    [Fact]
    public void SegmentsAreRecycled_soASteadyStreamStopsAllocating()
    {
        var ring = new ByteRing(segmentSize: 1024);
        var chunk = Pattern(1024);
        var buffer = new byte[1024];

        // Prime the pool.
        for (var i = 0; i < 50; i++)
        {
            ring.Write(chunk);
            ring.Read(buffer);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 500; i++)
        {
            ring.Write(chunk);
            ring.Read(buffer);
        }

        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // 500 KB of traffic through 1 KB segments. Without recycling this would allocate
        // hundreds of segments; a small allowance covers incidental test-harness noise.
        Assert.True(allocated < 64 * 1024, $"steady-state traffic allocated {allocated} bytes");
    }

    [Fact]
    public void ProducerAndConsumerOnDifferentThreads_loseNoBytesAndKeepOrder()
    {
        const int Total = 2_000_000;
        var ring = new ByteRing(segmentSize: 512);
        var produced = Pattern(Total);
        var consumed = new byte[Total];
        var consumedCount = 0;

        var producer = new Thread(() =>
        {
            var offset = 0;
            var rng = new Random(1);
            while (offset < Total)
            {
                var n = Math.Min(rng.Next(1, 4096), Total - offset);
                ring.Write(produced.AsSpan(offset, n));
                offset += n;
            }
        });

        producer.Start();

        var buffer = new byte[3000];
        while (consumedCount < Total)
        {
            var n = ring.Read(buffer);
            if (n == 0)
            {
                Thread.SpinWait(50);
                continue;
            }

            buffer.AsSpan(0, n).CopyTo(consumed.AsSpan(consumedCount));
            consumedCount += n;
        }

        Assert.True(producer.Join(TimeSpan.FromSeconds(30)), "producer did not finish");
        Assert.Equal(produced, consumed);
        Assert.True(ring.IsEmpty);
    }
}
