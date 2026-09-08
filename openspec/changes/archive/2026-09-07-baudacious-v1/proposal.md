## Why

Modern terminals are instant, and something was lost with that. Text that
arrives at 300 baud is *read* as it arrives; text that arrives at 1200 baud
has a cadence; a directory listing at 2400 baud is an event with a beginning
and an end. No current terminal reproduces this, and the ones that try do it
by sleeping between whole lines, which is exactly wrong — the character is
the unit, not the line.

Baudacious is a terminal emulator whose defining feature is that its output is
metered onto the screen at a real, user-selected baud rate, accurate enough
that a stopwatch agrees with it. Everything else in the product exists to
serve that.

## What Changes

This change bootstraps the entire product; there is no existing code.

**The baud governor (the reason the app exists)**
- User-selectable line rate: 110, 300, 1200, 2400, 4800, 9600, 19200, 38400,
  57600, 115200 baud, plus an unthrottled mode.
- Selectable framing (8N1 default, 7E1, 8N2), because framing determines
  bits-per-character and therefore the actual character rate. 8N1 at 1200
  baud is 120 char/sec; 8N2 at 1200 baud is 109.1 char/sec. Both are correct
  for their framing and Baudacious will honour the difference.
- Metering happens on the **byte** stream, ahead of terminal emulation. An
  escape sequence consumes wire time exactly as printable text does, so a
  syntax-colored prompt genuinely takes longer to paint than a plain one.
  This is the single most load-bearing decision in the design.
- The hosted shell runs at full speed and its output queues; the queue drains
  at line rate. A command that finishes in 20 ms may still be painting
  thirty seconds later, which is the authentic behaviour of a fast host on a
  slow line.
- Baud rate is changeable mid-session without disturbing the running shell.

**Escape hatches from the authenticity**
- `Ctrl+C` interrupts the shell *and* discards the queued backlog, so the
  user is never held hostage by output they have already abandoned.
- `Alt+F` fast-forwards: drains the entire pending queue at once without
  signalling the shell. This is the deliberate "I want to cheat and see it
  all now" control.
- Holding `Alt+Shift+F` runs the line at 10x until released, for when the
  user wants to skim rather than skip.

**Shell hosting**
- Shells are hosted through Windows ConPTY, behind a backend interface with
  two v1 implementations: `pwsh` and `cmd.exe`.
- Shell profiles are user-definable (executable, arguments, working
  directory, environment, startup commands).
- Baudacious injects sane startup defaults for pwsh (a simplified prompt, no
  predictive IntelliSense) because PSReadLine's per-keystroke full-line
  repaint is unbearable below 9600 baud. The injection is opt-out.

**Terminal emulation**
- An in-house VT100/VT220/xterm-subset parser built on the DEC/Williams state
  machine, feeding an in-house cell grid with scrollback.
- Enough xterm fidelity for pwsh and cmd.exe to be genuinely usable: SGR
  color including 256-color and truecolor, cursor addressing, scroll
  regions, erase/insert/delete, alternate screen buffer, OSC window title,
  bracketed paste, DECAWM autowrap.

**Display**
- Baudacious owns an OS window and renders text itself on the GPU. It is **not**
  hosted inside conhost or Windows Terminal. This is what makes the CRT
  aesthetic reachable at all.
- Text is drawn from a CP437 bitmap glyph atlas (IBM VGA 8x16) as a single
  instanced quad batch, one instance per visible cell.
- A post-process pass gives the display its CRT character. v1 scope: phosphor
  bloom, scanlines, and vignette, with selectable phosphor color (P1 green,
  P3 amber, P4 white). Barrel distortion, aperture-grille masking, and
  phosphor persistence/decay are explicitly deferred — but the render pipeline
  is structured so they are a shader change and nothing more.

**Local command layer**
- Client-side commands are bound to `Alt+<letter>`, following ProComm Plus and
  Telix. Alt chords are not ASCII, so they never contend with bytes the shell
  wants. `Ctrl+]` (telnet's own escape character, and unbound in both pwsh and
  cmd) drops into a local command prompt for keyboard-only operation.
- v1 commands: change baud, change framing, fast-forward, flush, phosphor
  color, font size, new/switch shell session, help overlay, quit.
- **Constraint**: apart from `Ctrl+C`, no plain `Ctrl+<letter>` chord is
  consumed by Baudacious. They all belong to the shell.

**Configuration**
- Human-editable config file under `%APPDATA%\Baudacious\`, hot-reloaded on save.
- Persists last-used baud rate, framing, phosphor, font size, window geometry,
  and shell profiles.

## Capabilities

### New Capabilities
- `baud-governor`: Byte-rate metering of the shell output stream at a selected
  baud rate and framing, including the backlog queue, the flush/fast-forward
  controls, and the timing accuracy guarantees.
- `shell-hosting`: Spawning and supervising shells through ConPTY behind a
  pluggable backend interface; shell profiles; window-resize propagation;
  process lifetime and exit handling.
- `terminal-emulation`: VT/xterm escape-sequence parsing, the cell grid,
  scrollback, and the supported-sequence contract.
- `display-rendering`: The GPU window, glyph atlas text rendering, the CRT
  post-process chain, phosphor palettes, and frame pacing.
- `input-and-local-commands`: Keyboard and mouse input translation to shell
  bytes, the `Alt+<letter>` local command layer, the `Ctrl+]` local prompt,
  selection and clipboard.
- `configuration`: Config file schema, defaults, hot reload, and session
  persistence.

### Modified Capabilities

None — this is the first change in the project.

## Impact

- **New repository content**: a .NET 9 solution. Projects: `Baudacious.Core`
  (governor, emulation, no I/O), `Baudacious.Pty` (ConPTY interop),
  `Baudacious.Render` (OpenGL), `Baudacious.App` (composition root), plus test
  projects.
- **New dependencies**: `Silk.NET.Windowing`, `Silk.NET.OpenGL`,
  `Silk.NET.Input`. Nothing else at runtime. ConPTY is reached by direct
  P/Invoke rather than a wrapper package, so the pty layer stays auditable
  and dependency-free.
- **Platform**: Windows 10 1809+ (the ConPTY floor) and a GPU with OpenGL 3.3.
  The core and emulation layers stay platform-neutral so a Unix `forkpty`
  backend is possible later, but no non-Windows support is claimed in v1.
- **Font licensing**: v1 ships an embedded CP437 bitmap atlas derived from a
  freely-redistributable IBM VGA font (VileR's Oldschool PC Font pack,
  CC BY-SA 4.0). Attribution obligation must be honoured in the about text.
- **Explicit non-goals for v1**: tabs and split panes, ssh/serial-port
  transports (the "baud" here is simulated, not a real COM port), real MS-DOS
  emulation, modem sound effects, Unicode beyond what the CP437 atlas covers,
  ligatures, image protocols (sixel/kitty), and accessibility/screen-reader
  support. Several of these are wanted eventually; none are v1.
