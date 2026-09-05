using System.Runtime.InteropServices;

namespace Baudacious.App;

/// <summary>
/// Turns on VT sequence processing for our own console output, so the escape sequences the
/// renderer emits are acted on rather than printed.
/// </summary>
/// <remarks>
/// Only needed because Phase 2 paints into a host console. Phase 3 owns its window and
/// rasterises text itself, and this goes away.
/// </remarks>
internal static partial class VirtualTerminal
{
    private const int StdOutputHandle = -11;
    private const uint EnableVirtualTerminalProcessing = 0x0004;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GetStdHandle(int nStdHandle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetConsoleMode(nint hConsoleHandle, out uint lpMode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetConsoleMode(nint hConsoleHandle, uint dwMode);

    public static void EnableOnStdout()
    {
        var handle = GetStdHandle(StdOutputHandle);
        if (handle == 0 || handle == -1)
        {
            return;
        }

        if (GetConsoleMode(handle, out var mode))
        {
            SetConsoleMode(handle, mode | EnableVirtualTerminalProcessing);
        }
    }
}
