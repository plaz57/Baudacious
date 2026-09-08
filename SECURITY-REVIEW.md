# Security review

**Date:** 2026-09-08
**Revision reviewed:** `5bf38e7` (main), plus the paste sanitization change made during the review
**Reviewer:** Claude Code, at the author's request
**Scope:** the whole repository — `src`, `spike`, `tools`, `tests`, build configuration, assets, and the non-code files

## Question asked

Does the code do anything beyond what is necessary to run a terminal window with the
desired effects?

## Answer

No. Every source file was read in full. Baudacious contains no network access, no
registry access, no telemetry, no dynamic code loading, no obfuscated or hidden text, and
no persistence beyond two INI files in `%APPDATA%\Baudacious`. The one paste-handling
gap found is fixed in this tree. The remaining items below are hardening suggestions,
not evidence of anything the program should not be doing.

## What the program touches

This is the complete list of ways Baudacious interacts with anything outside its own
process. Anything not on this list, it does not do.

| Interaction | Where | Notes |
|---|---|---|
| Launch the chosen shell under ConPTY | `Baudacious.Pty/PtySession.cs` | `CreateProcessW` with `EXTENDED_STARTUPINFO_PRESENT` only, handle inheritance off, environment inherited unchanged, child placed in a job object with `KILL_ON_JOB_CLOSE`. Executable resolved from `System32` (cmd, wsl) or `PATH` then `Program Files` (pwsh). |
| Read and write `baudacious.ini` and `state.ini` | `Baudacious.App/Config/ConfigStore.cs` | `baudacious.ini` is created once from a template and never rewritten. `state.ini` is rewritten on exit. Both live in `%APPDATA%\Baudacious` or the `--settings` directory. A `FileSystemWatcher` re-reads `baudacious.ini` on change. |
| Read and write the Windows clipboard | `Baudacious.App/Clipboard.cs` | Only on Ctrl+C with a selection (write) and Ctrl+V / Shift+Insert (read). No program-driven access: OSC 52 is not implemented. |
| Attach to the parent console | `Baudacious.App/ParentConsole.cs` | `AttachConsole(ATTACH_PARENT_PROCESS)` so `--help` has somewhere to print. |
| Query the desktop work area | `Baudacious.App/Desktop.cs` | `SystemParametersInfoW(SPI_GETWORKAREA)` to size the opening window. |
| Enable VT processing on stdout | `Baudacious.App/VirtualTerminal.cs` | Console-hosted diagnostic mode only. |
| Write PNG or text files to a user-named path | `shot`, `atlas`, `console --out` subcommands | Developer diagnostics, path supplied on the command line. |
| Rasterize a system font | `Baudacious.Render/GlyphAtlas.cs` | GDI+ via System.Drawing, to fill the handful of CP437 slots the bitmap faces lack. |
| OpenGL 3.3 through GLFW | `Baudacious.Render`, `Baudacious.App/TerminalWindow.cs` | Silk.NET bindings. Shaders are string constants compiled at startup. |

Native interop is confined to `kernel32.dll` and `user32.dll`. `Baudacious.Core` has no
I/O of any kind. The `spike` project is throwaway measurement code, is not referenced by
the application, and its only external action is a loopback `ping` used as a ConPTY
smoke test.

### Dependencies

Direct: Silk.NET.Windowing, Silk.NET.OpenGL, Silk.NET.Input (2.23.0), System.Drawing.Common
(9.0.0), and the xunit test stack. Transitive: the remaining Silk.NET assemblies at the
same version, `Ultz.Native.GLFW` 3.4.0 (which supplies `glfw3.dll`),
`Microsoft.Extensions.DependencyModel`, and `Microsoft.Win32.SystemEvents`. All direct
versions are exact. There is no lock file; see finding 6.

### Bundled assets

The three `.hex` font files contain only well-formed glyph rows: every line is
`CODEPOINT:32 hex digits` or, in unscii, `CODEPOINT:64 hex digits` for double-width glyphs
the loader skips. The ConPTY capture fixtures under `tests/fixtures` are recorded byte
streams consumed only by tests and the `meter` diagnostic.

## Terminal attack surface

A terminal emulator's specific risk is that the program it hosts controls the byte stream
it interprets. Output from `cat` of a hostile file, or from a hostile SSH server, can try to
drive the terminal. Baudacious handles this conservatively:

- **OSC:** only 0 and 2 (window title) are honored. No OSC 52 clipboard write, no OSC 8
  hyperlinks. Title text goes to the window title and nowhere else.
- **Report-back sequences:** the only bytes the terminal ever sends unprompted are the
  standard cursor-position report (DSR 6), status report (DSR 5), and device attributes
  (DA). There is no XTWINOPS title report (`CSI 21 t`), no DECRQSS, and DCS, SOS, PM and APC
  strings are consumed whole and discarded. Those omissions close the classic
  "set the title, then ask the terminal to type it back" injection.
- **Parser bounds:** 32 parameters, 4 intermediates, 4096-byte OSC buffer, numeric
  parameters clamped to 65535. Overflow drops the sequence rather than acting on part of it.
- **UTF-8:** invalid and truncated sequences become U+FFFD. Overlong encodings decode to a
  printable, never to a control, so they cannot smuggle ESC past the parser. C1 bytes in
  the ground state are treated as UTF-8, not as 8-bit controls.
- **Mouse reporting:** enabled only by request, and Shift always takes the mouse back for
  selection.
- **Local prompt:** nothing typed at the Ctrl+] prompt reaches the shell. Verified in the
  key dispatch order.

## Findings

Ranked by how much they matter. None is a case of the program doing something outside its
purpose.

### 1. Pasted text was not sanitized — **fixed**

`TerminalWindow.Paste` wrapped the clipboard in `ESC[200~` and `ESC[201~` when the shell
had bracketed paste on, but passed the clipboard contents through raw. A clipboard payload
containing its own `ESC[201~` ended the frame early, and everything after it arrived as
trusted keystrokes. Other control characters, such as Ctrl+C or a bare ESC, also passed
through.

**Fix:** `Baudacious.Core/Terminal/PasteText.cs` normalizes line endings to CR and removes
every other control character (C0 except TAB, DEL, C1) before anything is sent. This applies
whether or not bracketed paste is on. The status bar reports how many characters were
dropped. Both the shell paste and the local-prompt paste use it. Covered by
`tests/Baudacious.Core.Tests/PasteTextTests.cs`, and the requirement is recorded in
`openspec/specs/input-and-local-commands/spec.md`.

### 2. Startup and `fastecho` commands are typed into the shell as keystrokes — open, low

`TerminalSession.SendSetup` waits for the shell's first output plus 250 ms, then types
`Remove-Module PSReadLine` and a prompt override into stdin. The `fastecho` local command
does the same on a running shell. The text is benign, but delivery is timing-based at
startup, and `fastecho` sends the text to whatever is reading stdin at that moment. Inside
an SSH session, an editor, or a REPL, it lands there instead of in PowerShell.

**Suggestion:** note the behavior in the `fastecho` help text, or refuse the command when
the alternate screen buffer is active, which is the cheap signal that a full-screen
program owns the terminal.

### 3. Custom `[profile.*]` sections are parsed but never launched — open, functional

`SettingsReader.Profiles` reads `command`, `arguments` and `like` from the settings file,
but `ShellProfiles.Build` only knows `cmd`, `wsl` and `pwsh`, and anything else falls
through to `pwsh`. The README advertises the feature. Incidentally, this means the settings
file cannot currently name an arbitrary executable.

**Suggestion:** when this is wired up, validate that the command exists, show the resolved
path on the status bar when the session opens, and treat a profile whose command cannot be
found the same way a missing `pwsh` is treated today. A user-writable config file that
names an executable is normal for a terminal, but it should be visible when it acts.

### 4. WSL distribution name is interpolated into a quoted argument — open, low

`ShellProfiles.BuildWsl` builds `-d "{distribution}"`. A name containing a double quote
breaks out of the quoting. The comment says `--` separates WSL's switches from the
command, but no `--` is emitted. The value comes from the user's own command line or
settings file, so the exposure is to the user's own typos.

**Suggestion:** reject names containing `"`, and append the `--` the comment promises.

### 5. Unbounded repeat counts in scroll and line operations — open, robustness

`ESC[65535S` runs `Screen.ScrollUp` 65,535 times, each pushing a cloned row into
scrollback and trimming the list. A hostile program can already burn CPU, so this is a
stall rather than a vulnerability, but a five-byte sequence causing seconds of work is
worth closing.

**Suggestion:** clamp `S`, `T`, `L`, `M` counts to the scroll region height, as xterm does.

### 6. No NuGet lock file — open, supply chain

Direct package versions are exact, but nothing detects a changed transitive package or a
substituted feed between restores.

**Suggestion:** set `RestorePackagesWithLockFile` in `Directory.Build.props` and commit the
resulting `packages.lock.json` files.

### 7. Dead code that launches a process — open, hygiene

`ShellProfiles.ListDistributions` is the only `Process.Start` in the shipped code and
nothing calls it. Removing it makes "only ConPTY launches processes" true by inspection
rather than by tracing callers.

## Method

Every `.cs`, `.csproj`, `.props`, and `.py` file was read in full. The read was preceded
by a sweep for the API surfaces that would indicate behavior outside a terminal's remit:
networking, process creation, registry, environment mutation, reflection and assembly
loading, encoding and decoding helpers, native imports, and file writes. Each hit was then
read in context. Source files were also checked for zero-width, bidirectional-override
and other invisible Unicode characters, and none were found. Non-code files (OpenSpec
documents, the bundled skills under `.claude`, licenses, asset provenance) were scanned for
embedded commands or URLs pointing anywhere other than the documented upstream sources.

## Not covered

- Behavior of the hosted shells themselves, WSL, ConPTY, GLFW, or the GPU driver.
- Runtime fuzzing of the VT parser. The bounds above were verified by reading, and the
  existing replay tests exercise every recorded capture byte-at-a-time and all-at-once.
- The published single-file binary. The review is of source.
