using System.Runtime.InteropServices;

namespace Baudacious.ConPtySpike;

/// <summary>
/// Gives the host process a console of its own and hides its window.
/// </summary>
/// <remarks>
/// Phase 0 measured this the hard way: when the process hosting a pseudoconsole has no
/// console of its own, the hosted shell prints its banner and prompt and then exits with
/// status 0 within about 100 ms. Baudacious is a windowed application and has no console by
/// nature, so it has to allocate one and keep it off screen.
/// </remarks>
internal static partial class ConsoleAttachment
{
    private const int SW_HIDE = 0;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllocConsole();

    [LibraryImport("kernel32.dll")]
    private static partial nint GetConsoleWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(nint hWnd, int nCmdShow);

    /// <summary>Whether this process allocated its own console at startup.</summary>
    public static bool Allocated { get; private set; }

    public static string Describe() =>
        Allocated ? "allocated (hidden)"
        : GetConsoleWindow() != 0 ? "pre-existing"
        : "NONE";

    /// <summary>Returns true if a console was allocated, false if one already existed.</summary>
    public static bool EnsureHidden()
    {
        if (GetConsoleWindow() != 0)
        {
            return false;
        }

        if (!AllocConsole())
        {
            return false;
        }

        Allocated = true;
        var window = GetConsoleWindow();
        if (window != 0)
        {
            ShowWindow(window, SW_HIDE);
        }

        return true;
    }
}
