using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Baudacious.App;

/// <summary>
/// Borrows the console of whatever launched us, if there was one.
/// </summary>
/// <remarks>
/// Baudacious is a windowed application that also has a command line, and Windows makes you
/// choose: a console subsystem binary flashes up a console window when it is started from
/// Explorer or a shortcut, and a windows subsystem binary has nowhere to print
/// <c>--help</c>. Attaching to the parent's console gets both — output goes to the terminal
/// that started it and nothing appears when nothing started it from a terminal.
/// <para>
/// This has to run before anything touches <see cref="Console"/>, because the streams are
/// bound on first use and would otherwise be bound to nothing.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class ParentConsole
{
    private const uint AttachParentProcess = 0xFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    /// <summary>True if there is now a console to write to.</summary>
    public static bool Attach()
    {
        try
        {
            return AttachConsole(AttachParentProcess);
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }
}
