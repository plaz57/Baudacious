namespace Baudacious.Core.Timing;

/// <summary>
/// A monotonic clock. Everything time-dependent in <c>Baudacious.Core</c> takes one of
/// these rather than reading the clock directly, which is what makes the governor's
/// accuracy assertable in a unit test with no sleeps and no flakiness.
/// </summary>
public interface ITimeSource
{
    /// <summary>Current tick count from a monotonic source. Never wall-clock time.</summary>
    long Timestamp { get; }

    /// <summary>Ticks per second, for converting a tick delta to seconds.</summary>
    long Frequency { get; }
}
