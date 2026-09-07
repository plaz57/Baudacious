using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Baudacious.App;

/// <summary>
/// What the desktop will actually let a window be.
/// </summary>
/// <remarks>
/// Asked of Windows rather than of Silk.NET, for two reasons. Silk's
/// <c>Monitor.GetMainMonitor</c> needs its windowing platform registered, which has not
/// happened before the first window is created — in a single-file publish it throws
/// <c>PlatformNotSupportedException</c> outright, which is how this came to be written.
/// And <c>SPI_GETWORKAREA</c> answers a better question anyway: the area excluding the
/// taskbar, rather than the whole screen with a fudge factor subtracted.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class Desktop
{
    private const uint SpiGetWorkArea = 0x0030;

    /// <summary>
    /// Title bar and borders, which the work area includes and a client size does not.
    /// </summary>
    /// <remarks>
    /// Generous, and deliberately not measured: this is a bound on the opening size, not a
    /// layout, and being forty pixels conservative costs at most one row.
    /// </remarks>
    public const int Chrome = 48;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfoW(uint action, uint param, ref Rect data, uint update);

    /// <summary>
    /// The primary monitor's usable area in pixels, or null if Windows will not say.
    /// </summary>
    public static (int Width, int Height)? WorkArea()
    {
        try
        {
            var rect = default(Rect);
            if (!SystemParametersInfoW(SpiGetWorkArea, 0, ref rect, 0))
            {
                return null;
            }

            var width = rect.Right - rect.Left;
            var height = rect.Bottom - rect.Top;

            return width > 0 && height > 0 ? (width, height) : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }
}
