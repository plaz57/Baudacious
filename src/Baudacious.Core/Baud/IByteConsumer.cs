namespace Baudacious.Core.Baud;

/// <summary>
/// Receives the bytes the governor has released. In the finished product this is the VT
/// parser; in the Phase 1 harness it is a writer to stdout.
/// </summary>
/// <remarks>
/// An interface rather than a delegate because <c>ReadOnlySpan&lt;byte&gt;</c> cannot be
/// a generic type argument, and the governor must not allocate per tick.
/// </remarks>
public interface IByteConsumer
{
    void Consume(ReadOnlySpan<byte> bytes);
}
