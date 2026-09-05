using System.Diagnostics;

namespace Baudacious.Core.Timing;

/// <summary>
/// The real clock, backed by <see cref="Stopwatch"/>.
/// </summary>
/// <remarks>
/// <see cref="Stopwatch"/> and not <c>DateTime</c>: the governor must not speed up or
/// stall because the system clock was adjusted, an NTP correction landed, or daylight
/// saving changed.
/// </remarks>
public sealed class SystemTimeSource : ITimeSource
{
    public static readonly SystemTimeSource Instance = new();

    public long Timestamp => Stopwatch.GetTimestamp();

    public long Frequency => Stopwatch.Frequency;
}
