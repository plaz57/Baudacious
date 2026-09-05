using Baudacious.Core.Baud;

namespace Baudacious.Core.Tests;

/// <summary>Accumulates everything the governor releases, for assertion.</summary>
internal sealed class CollectingConsumer : IByteConsumer
{
    private readonly List<byte> _bytes = [];

    public int Count => _bytes.Count;

    public int Calls { get; private set; }

    public byte[] ToArray() => [.. _bytes];

    public void Consume(ReadOnlySpan<byte> bytes)
    {
        Calls++;
        _bytes.AddRange(bytes);
    }

    public void Reset()
    {
        _bytes.Clear();
        Calls = 0;
    }
}
