Phases follow `design.md` — Migration Plan. Every phase ends in something you
can run and watch.

## 1. Phase 0 — Measure ConPTY before building on it

- [x] 1.1 Create the solution and the six projects from design D12, targeting net10.0 (.NET 9 reached end of support in May 2026), with `Baudacious.Core` referencing nothing
- [x] 1.2 Write the ConPTY P/Invoke surface in `Baudacious.Pty`: `CreatePseudoConsole`, `ResizePseudoConsole`, `ClosePseudoConsole`, `InitializeProcThreadAttributeList`, `UpdateProcThreadAttribute`, `CreateProcessW`
- [x] 1.3 Launch `pwsh` through it and read its output to the console; prove the handle-close ordering shuts down cleanly with no hung reader thread
- [x] 1.4 Build the spike recorder: log every received byte with a microsecond timestamp to a capture file, and a hex/annotated dump viewer
- [x] 1.5 Capture and record the numbers for pwsh: bytes per keystroke, bytes per default prompt redraw, unsolicited repaint traffic at rest, and the same with predictions disabled and a simplified prompt
- [x] 1.6 Capture the same numbers for `cmd.exe`, plus captures of `dir /s`, a colorised `git status`, and a full-screen program entering the alternate buffer
- [x] 1.7 Write up the measurements in the change directory and decide whether D2 needs a repaint-coalescing escape hatch — if it does, amend the design before Phase 2
- [x] 1.8 Promote the captures to `tests/fixtures/conpty-captures/` as fixtures for Phases 1 and 2 (repo is not yet under version control; see summary)

## 2. Phase 1 — The governor, proven

- [x] 2.1 Define `ITimeSource` and its real (`Stopwatch`) and fake implementations
- [x] 2.2 Implement the framing table (8N1, 7E1, 8N2, 8E1) and the bits-per-character derivation, with 110 baud defaulting to 8N2
- [x] 2.3 Implement the SPSC ring buffer with pooled segments, unbounded growth, and no allocation on the steady-state path
- [x] 2.4 Implement `BaudGovernor`: credit accumulator, `ParseBudgetBytes` work cap, unspent-credit retention, and rate/framing changes that neither discard bytes nor reset credit
- [x] 2.5 Implement `Flush()` (discard the backlog) and `FastForward()` (unbounded credit for one tick) as distinct operations
- [x] 2.6 Implement the 10x rate multiplier as a held modifier on the credit calculation
- [x] 2.7 Expose backlog size and estimated drain time
- [x] 2.8 Deterministic unit tests on the fake clock: exact byte counts per rate and framing, fractional carry at 300 and 110 baud, no drift over 10,000 simulated frames, and correct catch-up after a simulated 250 ms stall
- [x] 2.9 Conformance test against the real clock: 3 s by default so the everyday suite stays quick, the spec's 60 s window under `BAUDACIOUS_CONFORMANCE_SECONDS=60`, asserting the 0.5% band either way
- [x] 2.10 Console harness that meters a Phase 0 capture file to stdout at a rate given on the command line — the first demo of the product's central claim
- [x] 2.11 Verify the harness with a stopwatch at 300, 1200, and 9600 baud

## 3. Phase 2 — Terminal emulation

- [x] 3.1 Implement the DEC/Williams parser state machine as an explicit table, resumable mid-sequence, with unrecognised and malformed sequences consumed silently
- [x] 3.2 Implement UTF-8 decoding with substitution on invalid sequences
- [x] 3.3 Implement the `Cell` struct, the row model with `SoftWrapped` and logical-line id, and the primary grid
- [x] 3.4 Implement cursor movement: CUP, CUU, CUD, CUF, CUB, CHA, VPA, HVP, DECSC/DECRC, IND, RI, NEL
- [x] 3.5 Implement erase and edit: ED, EL, IL, DL, ICH, DCH, ECH
- [x] 3.6 Implement the scroll region (DECSTBM), SU, and SD
- [x] 3.7 Implement SGR: attribute flags, the 16 base and 8 bright colors, 256-color indexed, and 24-bit truecolor, resolving to RGBA at write time
- [x] 3.8 Implement modes: DECAWM autowrap, DECTCEM cursor visibility, bracketed paste (2004), and application cursor-key mode
- [x] 3.9 Implement the alternate screen buffer (1047/1048/1049) with correct primary-buffer restoration and no scrollback pollution
- [x] 3.10 Implement tab stops (HTS, TBC, CHT) and the reports DSR, DA, DECSTR, RIS
- [x] 3.11 Implement OSC 0/2 window title parsing
- [x] 3.12 Implement the scrollback ring buffer with configurable capacity and bounded memory
- [x] 3.13 Implement resize with reflow, using wrap flags to re-wrap soft-wrapped lines and preserve hard breaks
- [x] 3.14 Replay tests over every Phase 0 capture, asserting on grid content rather than golden snapshot files (assertions proved more useful than opaque snapshots; a text renderer for the grid exists either way)
- [x] 3.15 The invariant property test: feeding a stream byte-at-a-time and all-at-once produces identical grids — run over every capture
- [x] 3.16 Property test resize round-trips: narrow → wide → narrow preserves content
- [x] 3.17 Extend the console harness to drive the parser and print the grid, so a metered capture can be watched as a terminal in the console

### Previewed early in the Phase 2 console harness

`Baudacious.App shell` already wires ConPTY -> ring -> governor -> parser -> screen with a
live shell and paints it into the host console. That pulls a working preview of several
later tasks forward, but **none of them are done in their real form** — the GPU window,
the glyph atlas and the CRT pass are still entirely ahead. Previewed only: the frame loop
(4.7), the shell profiles and fast echo (4.10, 4.10a), key encoding (5.1, 5.2), Ctrl+C
flush (5.4), the Alt chord layer (5.6, 5.7), the phosphor palettes (6.5), and the backlog
indicator (7.9).

## 4. Phase 3 — First light

- [x] 4.1 Add Silk.NET.Windowing, Silk.NET.OpenGL, Silk.NET.Input; open a window with a GL 3.3 core context and clear it
- [x] 4.2 Build the CP437 8x16 glyph atlas as a 128x256 R8 texture and its loader — Open Question 1 resolved to unscii-16 (public domain), embedded as a `.hex` resource
- [x] 4.3 Implement the instanced text shader and per-frame instance buffer: one instance per cell carrying position, glyph index, fg, bg, and flags
- [x] 4.4 Render the grid to an FBO at native cell resolution and blit it to the screen with nearest sampling and integer scaling
- [x] 4.5 Implement attribute rendering: bold, faint, underline, reverse, invisible, strikethrough, and blink
- [x] 4.6 Derive grid dimensions from the drawable area and the scaled cell size; fill the leftover margin with the background color
- [x] 4.7 Implement the frame loop with vsync, wall-clock governor ticks, and the idle drop to 10 Hz when nothing is pending or changing
- [x] 4.8 Implement the PTY reader and writer threads and wire ConPTY → ring → governor → parser → grid → renderer
- [x] 4.9 Propagate grid size to ConPTY via `ResizePseudoConsole` on launch and on resize, without disturbing queued bytes
- [x] 4.10b Host a Linux shell under WSL, with an optional distribution name. Measured at 1.0 bytes per keystroke, so no taming is applied
- [x] 4.10 Implement shell profiles, the pwsh and cmd defaults, and the pwsh readline taming with its per-profile opt-out
- [x] 4.10a Implement fast echo: `Remove-Module PSReadLine` for pwsh, default below the 4800 baud threshold, toggleable on a live session with `fastecho`, no-op for cmd and wsl
- [ ] 4.10b Verify by hand whether tab completion survives fast echo, and record the answer in phase-0-findings.md §4a (the Phase 0 probe was faulty)
- [x] 4.10c Implement the usable-rate-floor warning, once per session, explaining that echo cost accumulates rather than being a fixed delay
- [x] 4.11 Handle shell exit: drain the remaining backlog at rate, then show the exit status and offer a restart
- [x] 4.12 Implement session and process cleanup on close, verified with no orphaned processes
- [x] 4.13 Implement cursor rendering: block, underline, and bar shapes, configurable blink, hidden when DECTCEM says so
- [x] 4.14 Implement font scale changes at integer multiples with grid recalculation and shell notification
- [x] 4.15 Frame time verified: 200x60 renders in 0.974 ms against a 16.67 ms budget at 60 Hz — 17x headroom, measured with `shot --bench`
- [x] 4.16 Manual acceptance: confirmed by the user — 1200 baud reads correctly in the window, matching the console harness

## 5. Phase 4 — Input and local commands

- [x] 5.1 Implement key encoding: printable UTF-8, control codes, Enter, Backspace, Tab, Escape
- [x] 5.2 Implement special-key sequences for arrows, F1–F12, Home, End, Page Up/Down, Insert, and Delete, honouring application cursor-key mode
- [x] 5.3 Route input through the writer thread so the render loop never blocks on `WriteFile`
- [x] 5.4 Implement Ctrl+C: interrupt the shell, discard the backlog, reset the parser to ground while preserving SGR state
- [x] 5.5 Verify no plain Ctrl+letter chord other than Ctrl+C is consumed locally, with a test over the full alphabet
- [x] 5.6 Implement the Alt chord dispatcher, with unbound chords falling through to the shell as ESC + key
- [x] 5.7 Alt bindings B, F, Shift+F (held 10x), P, G, C, Z, X, N, S and font scale
- [x] 5.7a Require two presses of the quit chord within a short window, since one press kills a live shell and Alt+X sits beside bindings pressed casually (done early in the window; the console harness still quits on one press)
- [x] 5.8 Implement the overlay menus for baud rate, framing, and phosphor palette
- [x] 5.9 Alt+Z help overlay listing every binding and its effect (the config file path arrives with the config file in Phase 6)
- [x] 5.10 Implement the Ctrl+] local command prompt with history and completion
- [x] 5.11 Implement the local commands: `baud`, `framing`, `flush`, `phosphor`, `fontsize`, `crt`, `session new`, `help`, `quit`, with local errors on unrecognised input. **`session new` parses and is refused** until 5.18
- [x] 5.12 Implement mouse selection: drag, double-click word, triple-click logical line, Shift+drag extend, across screen and scrollback
- [x] 5.13 Implement copy with trailing-whitespace trimming and wrapped lines copied as one line
- [x] 5.14 Implement paste, unmetered, with bracketed paste markers when the mode is enabled
- [x] 5.15 Implement Ctrl+C as copy when a selection is active, sending nothing and preserving the backlog
- [x] 5.16 Mouse reporting passthrough for modes 1000/1002/1003 in both the legacy and SGR encodings, with shift+drag taking the mouse back for local selection
- [x] 5.17 Scrollback by wheel and shift+pgup/pgdn/home/end; the view stays put as output arrives, and the status bar shows how far back it is
- [x] 5.18 Implement multiple sessions with Alt+N and Alt+S
- [ ] 5.19 Manual acceptance: a full working session at 2400 baud using only the keyboard

## 6. Phase 5 — The CRT pass

- [x] 6.1 Restructure pass 2 to take its render target as a parameter, so a future persistence ping-pong needs no change here
- [x] 6.2 Implement the downsample and separable Gaussian blur chain into half- and quarter-resolution FBOs
- [x] 6.3 Implement the composite shader: additive bloom, horizontal scanlines, and edge vignette, each with an independent intensity uniform
- [x] 6.4 Bypass path implemented and measured: 0.974 ms with the pass, 0.327 ms without, at 200x60
- [x] 6.5 Implement the phosphor palettes: Rec. 709 luminance mapping to P1 green, P3 amber, and P4 white, with bold as an intensity boost and reverse swapping luminances
- [x] 6.6 Implement full-color mode bypassing the phosphor transform
- [x] 6.7 Tuned by eye against the rendered test patterns: bloom 0.55, scanline 0.30, vignette 0.35, ambient 0.02
- [x] 6.8 Legibility verified at 2x over the whole CP437 repertoire plus the confusable pairs (ILil1|! O0QoD S5 B8 G6 rn/m cl/d vv/w)
- [x] 6.9 Verified at 1x, 2x and 4x. Scanlines need 2x or more; at 1x every output row samples the same phase, so the effect degrades to a slight uniform dimming rather than banding
- [x] 6.10 Open Question 3 decided: no named presets — three intensities plus off is already the whole interface

## 7. Phase 6 — Configuration and polish

- [ ] 7.1 Implement the INI reader and writer with comment preservation
- [ ] 7.2 Define the config schema covering every setting the spec requires, and generate the commented default file on first run
- [ ] 7.3 Implement per-setting validation with fallback to defaults, reporting the offending value and its location without overwriting the file
- [ ] 7.4 Implement `state.ini`: persist rate, framing, phosphor, font scale, CRT settings, and window geometry on exit; restore on start; never write to `baudacious.ini`
- [ ] 7.5 Implement precedence: `state.ini` over `baudacious.ini` over built-in defaults
- [ ] 7.6 Implement hot reload with debounce, applying changes within one second and leaving running shells undisturbed
- [ ] 7.7 Implement broken-edit handling: keep the last good settings in effect and report the error
- [ ] 7.8 Make the local key bindings configurable
- [ ] 7.8a Bundle more than one bitmap typeface and make the choice a setting, applied live and persisted. unscii-16 reads as too heavy at 8x16 — its capitals have 2px stems. There is **no lighter face inside unscii**: the thin/alt/tall styles exist only at 8x8, and `unscii-8-tall` is the 8x8 with every row doubled, so it is bolder still. Candidates to evaluate, lightest first: Spleen 8x16 (BSD-2), Terminus `ter-u16n` (OFL 1.1), Tamsyn 8x16 (permissive custom), and VileR's Px437 IBM VGA 8x16 (CC BY-SA 4.0 — the authentic 1px-stem VGA look, but the only candidate that puts a share-alike asset in the tree). `HexFont`/`GlyphAtlas` already abstract the source, so the plumbing is a parameter, not a rewrite
- [ ] 7.9 Implement the backlog indicator, resolving Open Question 4 by how the drain estimate reads at low rates
- [ ] 7.10 Implement window geometry persistence and fullscreen toggling with grid recalculation
- [ ] 7.11 Resolve Open Question 1 and record the font attribution in the about text
- [ ] 7.12 Package as a self-contained single-file Windows executable and verify a clean-machine launch
- [ ] 7.13 Write the README with the baud-rate story, the key bindings, and the config file reference
