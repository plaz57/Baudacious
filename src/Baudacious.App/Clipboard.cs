using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Baudacious.App;

/// <summary>
/// Reads and writes the Windows clipboard as Unicode text.
/// </summary>
/// <remarks>
/// Direct Win32 rather than <c>System.Windows.Forms.Clipboard</c>, which requires a
/// single-threaded-apartment thread — something a GLFW render loop is not, and arranging
/// one just to copy a string would mean marshalling to a thread that exists for no other
/// reason.
/// </remarks>
[SupportedOSPlatform("windows")]
internal static partial class Clipboard
{
    private const uint UnicodeText = 13;
    private const uint GlobalMoveable = 0x0002;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(nint hWndNewOwner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint SetClipboardData(uint format, nint data);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial nint GetClipboardData(uint format);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalAlloc(uint flags, nuint bytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalLock(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint GlobalFree(nint handle);

    /// <summary>Puts text on the clipboard. Returns false rather than throwing.</summary>
    public static bool SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Another process can hold the clipboard open; a few attempts is normal practice.
        if (!TryOpen())
        {
            return false;
        }

        var block = nint.Zero;
        try
        {
            if (!EmptyClipboard())
            {
                return false;
            }

            var bytes = (nuint)((text.Length + 1) * sizeof(char));
            block = GlobalAlloc(GlobalMoveable, bytes);
            if (block == nint.Zero)
            {
                return false;
            }

            var pointer = GlobalLock(block);
            if (pointer == nint.Zero)
            {
                return false;
            }

            try
            {
                Marshal.Copy(text.ToCharArray(), 0, pointer, text.Length);
                Marshal.WriteInt16(pointer, text.Length * sizeof(char), 0);
            }
            finally
            {
                GlobalUnlock(block);
            }

            if (SetClipboardData(UnicodeText, block) == nint.Zero)
            {
                return false;
            }

            // Ownership passed to the clipboard; freeing it now would be a double free.
            block = nint.Zero;
            return true;
        }
        finally
        {
            if (block != nint.Zero)
            {
                GlobalFree(block);
            }

            CloseClipboard();
        }
    }

    /// <summary>Reads text from the clipboard, or null if there is none.</summary>
    public static string? GetText()
    {
        if (!IsClipboardFormatAvailable(UnicodeText) || !TryOpen())
        {
            return null;
        }

        try
        {
            var handle = GetClipboardData(UnicodeText);
            if (handle == nint.Zero)
            {
                return null;
            }

            var pointer = GlobalLock(handle);
            if (pointer == nint.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    private static bool TryOpen()
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            if (OpenClipboard(nint.Zero))
            {
                return true;
            }

            Thread.Sleep(15);
        }

        return false;
    }
}
