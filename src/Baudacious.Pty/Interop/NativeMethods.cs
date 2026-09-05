using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Baudacious.Pty.Interop;

/// <summary>
/// The Win32 surface needed to host a console application through a pseudoconsole.
/// See design.md D4 for why this is hand-written interop rather than a package:
/// the hard part of ConPTY is handle lifetime, and wrapping the calls hides the
/// very thing that has to be got right.
/// </summary>
internal static unsafe partial class NativeMethods
{
    private const string Kernel32 = "kernel32.dll";

    // ---- Pseudoconsole -----------------------------------------------------

    /// <summary>Start with the cursor state the parent console had.</summary>
    internal const uint PSEUDOCONSOLE_INHERIT_CURSOR = 0x00000001;

    [LibraryImport(Kernel32)]
    internal static partial int CreatePseudoConsole(
        Coord size,
        nint hInput,
        nint hOutput,
        uint dwFlags,
        out nint phPC);

    [LibraryImport(Kernel32)]
    internal static partial int ResizePseudoConsole(nint hPC, Coord size);

    /// <summary>
    /// Terminates the pseudoconsole. This call BLOCKS until ConPTY has flushed
    /// its pending output to the output pipe, so the pipe must still be being
    /// drained when it is made. Closing the reader first deadlocks here.
    /// </summary>
    [LibraryImport(Kernel32)]
    internal static partial void ClosePseudoConsole(nint hPC);

    // ---- Process creation --------------------------------------------------

    internal const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    internal const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    internal const uint CREATE_NO_WINDOW = 0x08000000;

    /// <summary>
    /// PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE. This is the attribute that hands the
    /// child a pseudoconsole instead of inheriting ours, and it is the whole
    /// reason ConPTY hosting works at all.
    /// </summary>
    internal static readonly nint PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool InitializeProcThreadAttributeList(
        nint lpAttributeList,
        int dwAttributeCount,
        int dwFlags,
        ref nint lpSize);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool UpdateProcThreadAttribute(
        nint lpAttributeList,
        uint dwFlags,
        nint attribute,
        nint lpValue,
        nint cbSize,
        nint lpPreviousValue,
        nint lpReturnSize);

    [LibraryImport(Kernel32)]
    internal static partial void DeleteProcThreadAttributeList(nint lpAttributeList);

    [LibraryImport(Kernel32, EntryPoint = "CreateProcessW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreateProcess(
        char* lpApplicationName,
        char* lpCommandLine,
        nint lpProcessAttributes,
        nint lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        void* lpEnvironment,
        char* lpCurrentDirectory,
        StartupInfoEx* lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    // ---- Pipes and handles -------------------------------------------------

    /// <summary>
    /// Raw handles rather than <c>out SafeFileHandle</c>: the pipe write end must stay
    /// open for the entire life of the session, and handing its lifetime to a marshaller
    /// is how it ends up closed early. A closed write end is indistinguishable from
    /// end-of-input to the hosted shell, which then exits with status 0 the moment it
    /// first reads — a very quiet failure.
    /// </summary>
    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CreatePipe(
        out nint hReadPipe,
        out nint hWritePipe,
        nint lpPipeAttributes,
        uint nSize);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint hObject);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool WriteFile(
        nint hFile,
        byte* lpBuffer,
        uint nNumberOfBytesToWrite,
        out uint lpNumberOfBytesWritten,
        nint lpOverlapped);

    // ---- Process lifetime --------------------------------------------------

    internal const uint INFINITE = 0xFFFFFFFF;

    [LibraryImport(Kernel32, SetLastError = true)]
    internal static partial uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetExitCodeProcess(nint hProcess, out uint lpExitCode);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TerminateProcess(nint hProcess, uint uExitCode);

    // ---- Job objects -------------------------------------------------------
    // A job object is how the spec's "no orphaned processes" requirement is met:
    // the child and everything it spawns die with us, even on a hard kill.

    internal const int JobObjectExtendedLimitInformation = 9;
    internal const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;

    [LibraryImport(Kernel32, EntryPoint = "CreateJobObjectW", SetLastError = true)]
    internal static partial nint CreateJobObject(nint lpJobAttributes, char* lpName);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetInformationJobObject(
        nint hJob,
        int jobObjectInformationClass,
        void* lpJobObjectInformation,
        uint cbJobObjectInformationLength);

    [LibraryImport(Kernel32, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AssignProcessToJobObject(nint hJob, nint hProcess);
}
