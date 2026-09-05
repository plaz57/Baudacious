using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Baudacious.Pty.Interop;
using Microsoft.Win32.SafeHandles;

namespace Baudacious.Pty;

/// <summary>
/// A shell hosted through a Windows pseudoconsole, presented as a bidirectional byte
/// stream with a resizable character grid — the session shape required by
/// specs/shell-hosting.
/// </summary>
/// <remarks>
/// <para><b>Handle lifetime is the whole difficulty here.</b> Two orderings matter:</para>
/// <list type="number">
/// <item>
/// The ConPTY-side pipe ends are closed immediately after
/// <c>CreatePseudoConsole</c>, because ConPTY duplicates them. Holding on to our copy
/// of the output write handle would mean the reader never sees end-of-stream and the
/// reader thread hangs forever on shutdown.
/// </item>
/// <item>
/// <c>ClosePseudoConsole</c> blocks until ConPTY has flushed everything it has queued
/// into the output pipe, so the reader thread must still be draining when it is
/// called. Stopping the reader first is a deadlock. <see cref="Dispose"/> therefore
/// closes stdin, lets the shell exit, calls <c>ClosePseudoConsole</c> while the reader
/// is still live, and only then joins the reader.
/// </item>
/// </list>
/// <para>
/// Consequently <see cref="Dispose"/> must never be called from the reader thread —
/// that is, never from inside <see cref="IPtyOutputSink"/>.
/// </para>
/// </remarks>
public sealed class PtySession : IDisposable
{
    private static readonly TimeSpan ShellExitGrace = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ReaderJoinGrace = TimeSpan.FromSeconds(5);

    private readonly IPtyOutputSink _sink;
    private readonly int _readBufferSize;
    private readonly Thread _readerThread;
    private readonly Lock _resizeLock = new();

    private nint _pseudoConsole;
    private nint _attributeList;
    private nint _processHandle;
    private nint _threadHandle;
    private nint _jobHandle;

    private nint _inputWriteRaw;
    private SafeFileHandle _inputWrite = null!;
    private SafeFileHandle _outputRead = null!;
    private FileStream _inputStream = null!;

    private int _disposed;

    private PtySession(IPtyOutputSink sink, int readBufferSize)
    {
        _sink = sink;
        _readBufferSize = readBufferSize;
        _readerThread = new Thread(ReaderLoop)
        {
            Name = "baudacious-pty-reader",
            IsBackground = true,
        };
    }

    /// <summary>Win32 error from the most recent write, or 0 if it succeeded.</summary>
    public int LastWriteError { get; private set; }

    /// <summary>Total bytes accepted by WriteFile on the input pipe.</summary>
    public long BytesWritten { get; private set; }

    /// <summary>Win32 error from AssignProcessToJobObject, or 0 if it succeeded.</summary>
    public int JobAssignmentError { get; private set; }

    /// <summary>Process id of the hosted shell.</summary>
    public int ProcessId { get; private set; }

    /// <summary>Current grid width in character cells.</summary>
    public short Columns { get; private set; }

    /// <summary>Current grid height in character cells.</summary>
    public short Rows { get; private set; }

    /// <summary>Launches a shell attached to a new pseudoconsole and starts reading it.</summary>
    public static PtySession Start(PtyStartInfo startInfo, IPtyOutputSink sink)
    {
        ArgumentNullException.ThrowIfNull(startInfo);
        ArgumentNullException.ThrowIfNull(sink);

        var session = new PtySession(sink, startInfo.ReadBufferSize);
        try
        {
            session.Launch(startInfo);
        }
        catch
        {
            session.Dispose();
            throw;
        }

        session._readerThread.Start();
        return session;
    }

    private unsafe void Launch(PtyStartInfo startInfo)
    {
        Columns = startInfo.Columns;
        Rows = startInfo.Rows;

        // Two pipes, one each way. We keep inputWrite and outputRead; ConPTY takes the
        // other two ends. Handles stay raw until we have decided who owns them.
        if (!NativeMethods.CreatePipe(out var inputRead, out var inputWrite, 0, 0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe (input) failed.");
        }

        if (!NativeMethods.CreatePipe(out var outputRead, out var outputWrite, 0, 0))
        {
            NativeMethods.CloseHandle(inputRead);
            NativeMethods.CloseHandle(inputWrite);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreatePipe (output) failed.");
        }

        // dwFlags is deliberately 0. PSEUDOCONSOLE_INHERIT_CURSOR makes ConPTY probe
        // the terminal for its cursor position (ESC[6n) during startup and withhold all
        // output until answered — and worse, the hosted shell then reaches end-of-input
        // and exits with status 0 the moment it first reads, showing a banner and prompt
        // before quietly dying. The flag only exists to carry a parent console's cursor
        // position across, which means nothing to an application that owns its own
        // window and starts on a fresh grid. Measured in Phase 0; see the writeup.
        var hr = NativeMethods.CreatePseudoConsole(
            new Coord(startInfo.Columns, startInfo.Rows),
            inputRead,
            outputWrite,
            dwFlags: 0,
            out _pseudoConsole);

        if (hr != 0)
        {
            NativeMethods.CloseHandle(inputRead);
            NativeMethods.CloseHandle(outputWrite);
            NativeMethods.CloseHandle(inputWrite);
            NativeMethods.CloseHandle(outputRead);
            Marshal.ThrowExceptionForHR(hr);
        }

        _inputWriteRaw = inputWrite;
        _inputWrite = new SafeFileHandle(inputWrite, ownsHandle: true);
        _outputRead = new SafeFileHandle(outputRead, ownsHandle: true);

        // ConPTY duplicated both of its ends into conhost, so our copies go now —
        // matching Microsoft's EchoCon sample, which closes them before CreateProcess.
        // Releasing the output write end is mandatory: while we hold a copy, the reader
        // can never observe end-of-stream and shutdown hangs waiting to join it.
        NativeMethods.CloseHandle(inputRead);
        NativeMethods.CloseHandle(outputWrite);

        CreateJob();
        StartProcess(startInfo);
    }

    private unsafe void CreateJob()
    {
        // Kill-on-close so the shell and everything it spawned die with Baudacious even
        // if Baudacious itself is killed. specs/shell-hosting: no orphaned processes.
        _jobHandle = NativeMethods.CreateJobObject(0, null);
        if (_jobHandle == 0)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateJobObject failed.");
        }

        var limits = new JobObjectExtendedLimitInformation();
        limits.BasicLimitInformation.LimitFlags = NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

        if (!NativeMethods.SetInformationJobObject(
                _jobHandle,
                NativeMethods.JobObjectExtendedLimitInformation,
                &limits,
                (uint)sizeof(JobObjectExtendedLimitInformation)))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetInformationJobObject failed.");
        }
    }

    private unsafe void StartProcess(PtyStartInfo startInfo)
    {
        var startupInfo = new StartupInfoEx();
        startupInfo.StartupInfo.cb = sizeof(StartupInfoEx);

        // Size the attribute list, allocate it, then fill it in. One attribute: the
        // pseudoconsole.
        nint listSize = 0;
        NativeMethods.InitializeProcThreadAttributeList(0, 1, 0, ref listSize);

        _attributeList = Marshal.AllocHGlobal(listSize);
        if (!NativeMethods.InitializeProcThreadAttributeList(_attributeList, 1, 0, ref listSize))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "InitializeProcThreadAttributeList failed.");
        }

        startupInfo.lpAttributeList = _attributeList;

        if (!NativeMethods.UpdateProcThreadAttribute(
                _attributeList,
                0,
                NativeMethods.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                _pseudoConsole,
                nint.Size,
                0,
                0))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "UpdateProcThreadAttribute failed.");
        }

        // CreateProcessW may write to lpCommandLine, so it gets a mutable buffer.
        var commandLine = BuildCommandLine(startInfo).ToCharArray();
        var environment = BuildEnvironmentBlock(startInfo.Environment);

        // EXTENDED_STARTUPINFO_PRESENT only. Console-creation flags such as
        // CREATE_NO_WINDOW, CREATE_NEW_CONSOLE and DETACHED_PROCESS all give the child
        // a console of its own, which silently defeats the pseudoconsole attribute:
        // the child attaches somewhere else, our output pipe stays empty, and closing
        // stdin never reaches it.
        var flags = NativeMethods.EXTENDED_STARTUPINFO_PRESENT;
        if (environment is not null)
        {
            flags |= NativeMethods.CREATE_UNICODE_ENVIRONMENT;
        }

        bool created;
        ProcessInformation processInfo;
        fixed (char* commandLinePtr = commandLine)
        fixed (char* workingDirPtr = startInfo.WorkingDirectory)
        fixed (char* environmentPtr = environment)
        {
            created = NativeMethods.CreateProcess(
                lpApplicationName: null,
                lpCommandLine: commandLinePtr,
                lpProcessAttributes: 0,
                lpThreadAttributes: 0,
                bInheritHandles: false,
                dwCreationFlags: flags,
                lpEnvironment: environmentPtr,
                lpCurrentDirectory: workingDirPtr,
                lpStartupInfo: &startupInfo,
                lpProcessInformation: out processInfo);
        }

        if (!created)
        {
            var error = Marshal.GetLastWin32Error();
            throw new Win32Exception(
                error,
                $"Could not start '{startInfo.Executable}'. CreateProcess failed with error {error}.");
        }

        _processHandle = processInfo.hProcess;
        _threadHandle = processInfo.hThread;
        ProcessId = processInfo.dwProcessId;

        if (!NativeMethods.AssignProcessToJobObject(_jobHandle, _processHandle))
        {
            JobAssignmentError = Marshal.GetLastWin32Error();
        }
    }

    private static string BuildCommandLine(PtyStartInfo startInfo) =>
        string.IsNullOrEmpty(startInfo.Arguments)
            ? $"\"{startInfo.Executable}\""
            : $"\"{startInfo.Executable}\" {startInfo.Arguments}";

    /// <summary>
    /// Builds a CREATE_UNICODE_ENVIRONMENT block: NUL-separated KEY=VALUE pairs with a
    /// trailing double NUL. Returns null to inherit the environment unchanged.
    /// </summary>
    private static char[]? BuildEnvironmentBlock(IReadOnlyDictionary<string, string>? overrides)
    {
        if (overrides is null || overrides.Count == 0)
        {
            return null;
        }

        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            merged[(string)entry.Key] = (string?)entry.Value ?? string.Empty;
        }

        foreach (var (key, value) in overrides)
        {
            merged[key] = value;
        }

        var builder = new StringBuilder();
        foreach (var (key, value) in merged.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
        {
            builder.Append(key).Append('=').Append(value).Append('\0');
        }

        builder.Append('\0');
        return builder.ToString().ToCharArray();
    }

    /// <summary>
    /// Sends bytes to the shell's standard input. Never metered: input travels at full
    /// speed, and it is the echo coming back that the baud governor slows down.
    /// </summary>
    public void Write(ReadOnlySpan<byte> bytes)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        unsafe
        {
            fixed (byte* p = bytes)
            {
                if (!NativeMethods.WriteFile(_inputWriteRaw, p, (uint)bytes.Length, out var written, 0))
                {
                    LastWriteError = Marshal.GetLastWin32Error();
                    return;
                }

                LastWriteError = 0;
                BytesWritten += written;
            }
        }
    }

    /// <summary>Tells ConPTY the grid changed. ConPTY's width, not ours, decides where the shell wraps.</summary>
    public void Resize(short columns, short rows)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, (short)1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, (short)1);

        lock (_resizeLock)
        {
            if (Volatile.Read(ref _disposed) != 0 || _pseudoConsole == 0)
            {
                return;
            }

            var hr = NativeMethods.ResizePseudoConsole(_pseudoConsole, new Coord(columns, rows));
            if (hr != 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            Columns = columns;
            Rows = rows;
        }
    }

    /// <summary>Blocks until the hosted shell exits, then returns its exit code.</summary>
    public int WaitForExit()
    {
        NativeMethods.WaitForSingleObject(_processHandle, NativeMethods.INFINITE);
        return GetExitCode();
    }

    /// <summary>The shell's exit code, or null while it is still running.</summary>
    public int? TryGetExitCode()
    {
        const uint StillActive = 259;
        if (_processHandle == 0 || !NativeMethods.GetExitCodeProcess(_processHandle, out var code))
        {
            return null;
        }

        return code == StillActive ? null : unchecked((int)code);
    }

    private int GetExitCode() =>
        NativeMethods.GetExitCodeProcess(_processHandle, out var code) ? unchecked((int)code) : -1;

    private void ReaderLoop()
    {
        var buffer = new byte[_readBufferSize];
        try
        {
            using var output = new FileStream(_outputRead, FileAccess.Read, bufferSize: 1, isAsync: false);
            while (true)
            {
                int read;
                try
                {
                    read = output.Read(buffer, 0, buffer.Length);
                }
                catch (IOException)
                {
                    // Broken pipe: ConPTY went away. Normal shutdown.
                    break;
                }

                if (read <= 0)
                {
                    break;
                }

                // Timestamp the instant the bytes surfaced, before the sink gets a
                // chance to add its own latency to the measurement.
                _sink.OnBytes(buffer.AsSpan(0, read), Stopwatch.GetTimestamp());
            }
        }
        catch (ObjectDisposedException)
        {
            // Handle closed underneath us during teardown.
        }
        finally
        {
            _sink.OnClosed();
        }
    }

    /// <summary>
    /// Shuts the session down in the one ordering that neither deadlocks nor leaks.
    /// Must not be called from the reader thread — see the class remarks.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // 1. Close stdin. pwsh and cmd both exit on end-of-input, which lets the shell
        //    tear itself down cleanly rather than being killed mid-write.
        try
        {
            _inputStream?.Dispose();
        }
        catch (IOException)
        {
            // Already broken; nothing to do.
        }

        _inputWrite?.Dispose();

        // 2. Give it a moment to go on its own, then insist.
        if (_processHandle != 0)
        {
            var waited = NativeMethods.WaitForSingleObject(_processHandle, (uint)ShellExitGrace.TotalMilliseconds);
            if (waited != 0)
            {
                NativeMethods.TerminateProcess(_processHandle, 1);
            }
        }

        // 3. ClosePseudoConsole blocks until ConPTY's pending output has been flushed
        //    into the pipe, so the reader thread has to still be draining right now.
        //    This is why the join below comes after, not before.
        if (_pseudoConsole != 0)
        {
            NativeMethods.ClosePseudoConsole(_pseudoConsole);
            _pseudoConsole = 0;
        }

        // 4. ConPTY has now released its dup of the output write handle, so the reader
        //    sees end-of-stream and returns on its own.
        if (_readerThread.IsAlive && !_readerThread.Join(ReaderJoinGrace))
        {
            // Should not happen; if it does, the ordering above has regressed.
            Debug.Fail("PTY reader thread did not exit after ClosePseudoConsole.");
        }

        _outputRead?.Dispose();

        if (_attributeList != 0)
        {
            NativeMethods.DeleteProcThreadAttributeList(_attributeList);
            Marshal.FreeHGlobal(_attributeList);
            _attributeList = 0;
        }

        CloseHandleIfOpen(ref _threadHandle);
        CloseHandleIfOpen(ref _processHandle);

        // 5. Last: closing the job kills anything the shell left behind.
        CloseHandleIfOpen(ref _jobHandle);
    }

    private static void CloseHandleIfOpen(ref nint handle)
    {
        if (handle != 0)
        {
            NativeMethods.CloseHandle(handle);
            handle = 0;
        }
    }
}
