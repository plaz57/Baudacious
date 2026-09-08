## Context

Greenfield repository; no existing code to accommodate. See `proposal.md` — Why
for motivation and the `specs/` directory for the behaviour contract.

Three constraints shape everything below.

**The binding constraint is timing, not throughput.** Even 115200 baud is 11.5
KB/s, which is nothing. The entire engineering difficulty is releasing bytes at
a *steady, verifiable* rate — including rates so slow that a 60 Hz frame is
entitled to a fraction of a byte.

**A CRT look requires owning the pixels.** No console host will apply a
post-process shader over its text. This forces Baudacious to be a windowed
application that rasterises text itself, which is a large chunk of v1's cost
and the reason the emulation and rendering capabilities exist at all.

**ConPTY is itself a terminal emulator, not a pipe.** `CreatePseudoConsole`
gives the child process a real console attached to an in-memory screen buffer;
what Baudacious receives is ConPTY's *rendering* of that buffer as VT sequences,
not the shell's raw output. It regenerates and normalises sequences, and it
repaints regions rather than forwarding writes verbatim. Every byte-count claim
in this project is therefore a claim about ConPTY's output, not the shell's.
This is fine — it is deterministic and it is what any Windows terminal sees —
but it means byte volume per keystroke is a property of ConPTY that has to be
*measured*, not assumed. Phase 0 exists to measure it.

Platform floor: Windows 10 1809 (ConPTY), OpenGL 3.3, .NET 10 (.NET 9 reached
end of support in May 2026).

## Goals / Non-Goals

**Goals:**

- A metering mechanism whose accuracy can be asserted in a unit test with no
  sleeps and no flakiness, and independently confirmed with a stopwatch.
- A render pipeline where adding barrel distortion or phosphor persistence later
  is a change to one shader and nothing else.
- A `Baudacious.Core` assembly with no I/O and no graphics: governor, VT parser,
  cell grid, and scrollback are pure, deterministic, and fully unit-testable.
- Byte-for-byte identical final grid state whether a stream is fed all at once
  or one byte per frame. This is the invariant that keeps the governor from
  becoming a source of emulation bugs.

**Non-Goals for this design:**

- Cross-platform PTY support. The layering permits it; v1 does not implement it.
- GPU API abstraction. One OpenGL 3.3 backend, written plainly. No renderer
  abstraction layer for a second API that does not exist.
- Sub-frame timing fidelity. See D3 — it is neither achievable nor observable.
- A settings GUI. The config file and the local command prompt are the interface.

## Decisions

### D1. Own window, OpenGL 3.3 via Silk.NET

Silk.NET.Windowing (GLFW backend) for the window and input, Silk.NET.OpenGL for
rendering. Three NuGet packages, no XAML, no content pipeline, AOT-compatible.

*Why OpenGL 3.3:* it is the lowest version providing instanced arrays and
framebuffer objects, which is the whole of what the design needs. Direct3D 11
would add a Windows lock-in with no compensating capability, and modern
Vulkan/D3D12 would add explicit synchronisation and pipeline management to draw
twelve thousand textured quads.

*Alternatives rejected:*
- **Console-hosted with ANSI passthrough.** Fastest path to a working baud
  meter, but the CRT aesthetic becomes permanently unreachable and the renderer
  is thrown away to get there. Rejected on the user's explicit long-term intent.
- **Avalonia / WPF + SkiaSharp.** A retained-mode layout system is the wrong
  shape for repainting an entire character grid every frame, and shader access
  is second-class in both. Would make the chrome easier and the core harder.
- **Veldrid.** A good abstraction, but maintenance has been intermittent and the
  abstraction buys nothing for a single-backend app.
- **MonoGame / raylib-cs.** Game-framework assumptions (content pipeline, fixed
  update loop) that would be worked around rather than used.

*Reversibility:* "Baudacious owns its window" is load-bearing — everything else
assumes it. "The backend is OpenGL" is not; `Baudacious.Render` is reachable only
through a narrow interface (`Present(GridSnapshot, DisplaySettings)`) and could
be replaced without touching core or app code.

### D2. The governor meters bytes, ahead of the VT parser

The pipeline is fixed as:

```
ConPTY read thread ──▶ SPSC ring buffer ──▶ BaudGovernor ──▶ VtParser ──▶ CellGrid ──▶ Renderer
     (blocking)          (unbounded)         (per frame)      (per byte)     (state)      (GPU)
```

The governor releases *raw bytes*. Escape-sequence bytes, control characters,
and UTF-8 continuation bytes each cost one character of wire time.

*Why:* a UART has no idea what ANSI is. Metering decoded glyphs instead would
make `ESC [ 3 1 m` free, so colored text would arrive at the same cadence as
plain text — and it is precisely the *unevenness* caused by escape traffic that
made a 1200-baud session feel the way it did. The alternative is cheaper to
build and visibly wrong.

*Measured in Phase 0, and the margin is wide:* across the interactive captures,
escape sequences are **28–48% of the byte stream** — 47.9% for tamed pwsh
keystrokes, 37.3% for a colored `git status`. Metering decoded glyphs instead
would deliver a pwsh session at roughly twice the speed a real line would, and
would flatten exactly the uneven cadence the product exists to reproduce.

*The consequence, and its fix:* what is expensive is not the prompt — default
prompts measure 6–21 bytes — but **typing**. PSReadLine repaints the whole input
line on every keystroke: 96.9 bytes per key stock, 28.5 fully tamed, against 1.0
for cmd.exe. That cost is not a fixed latency, it is wire time *generated* per
key, so it accumulates: at 4 characters/second and 1200 baud, tamed pwsh produces
~950 ms of echo for every second spent typing and the backlog grows for as long
as the user keeps going.

Incremental taming cannot fix this — ~28 bytes is PSReadLine's floor, because
full-line repaint is its design. **Removing PSReadLine can**: PowerShell then
falls back to the console host's cooked line input, the same discipline cmd.exe
uses, measured at exactly 1.0 bytes per keystroke. That is the "fast echo" mode
in `specs/shell-hosting`, default below 4800 baud and toggleable live. See
`phase-0-findings.md` §4a.

*This is the most load-bearing decision in the project.* Reversing it would
invalidate the governor, the parser boundary, and most of the timing tests.

### D3. Wall-clock credit accumulator; never a sleep, never a frame count

Per character framing:

| Framing | Bits/char | Rationale |
|---------|-----------|-----------|
| 8N1 | 10 | 1 start + 8 data + 1 stop. The modern default. |
| 7E1 | 10 | 1 start + 7 data + 1 parity + 1 stop. Classic BBS/mainframe. |
| 8N2 | 11 | 1 start + 8 data + 2 stop. |
| 8E1 | 11 | 1 start + 8 data + 1 parity + 1 stop. |

Release rate is `baud / bitsPerChar`. Each frame:

```
elapsedTicks  = clock.Timestamp - lastTick     // monotonic, never DateTime
creditUnits  += elapsedTicks * baud * speedMultiplier
n             = min(creditUnits / (bitsPerChar * clockFrequency), ParseBudgetBytes)
release(n)
creditUnits  -= n * (bitsPerChar * clockFrequency)   // only what was actually spent
if (queueEmpty) creditUnits = 0                      // an idle line banks no wire time
```

**Credit is an exact integer fraction, not a double.** Written the obvious way —
accumulating `elapsedSeconds * bytesPerSecond` into a `double` — the last byte of
every second is lost to a ~1e-13 shortfall, so 300 baud delivers 29 bytes per
second instead of 30, and the error compounds across a session. Holding credit as
`creditUnits / (bitsPerChar x clockFrequency)` in `long` arithmetic makes the
release count exactly right, which is also what lets the tests assert equality
instead of tolerance. (Found by Phase 1's own tests failing 29 ≠ 30; the fix is in
`BaudGovernor`.) The speed multiplier is an `int` for the same reason.

Two consequences fall out of the same arithmetic. A framing change has to rescale
accrued credit, since the denominator moves with `bitsPerChar`. And credit must be
zeroed whenever the queue drains, or a quiet minute banks enough wire time to dump
the next burst instantly — the opposite of what is being reproduced.

Fractional credit carries over, which is what makes slow rates exact rather than
approximate. Per-frame budgets at 60 Hz:

| Baud (8N1) | Bytes/sec | Bytes/frame @60 Hz |
|-----------|-----------|--------------------|
| 110 (8N2) | 10.0 | 0.167 |
| 300 | 30 | 0.5 |
| 1200 | 120 | 2 |
| 2400 | 240 | 4 |
| 9600 | 960 | 16 |
| 38400 | 3840 | 64 |
| 115200 | 11520 | 192 |

At 300 baud that is one byte on alternating frames — 33.3 ms apart, perceptible
and correct. 110 baud gets one byte every sixth frame, which is exactly how an
ASR-33 felt. (110 baud is defined here as 8N2, because two stop bits is what the
Teletypes actually used; 110/11 = 10 characters per second exactly, which is why
that framing existed.)

*Why not a high-resolution timer thread releasing individual bytes:* at 115200
baud a byte is due every 87 µs. Windows timer resolution is ~0.5 ms at best even
with `timeBeginPeriod(1)`, so per-byte scheduling is unachievable — and
pointless, because a 60 Hz display cannot show two events 87 µs apart as
distinct. Frame-quantised release is as accurate as the display can represent.

*Why elapsed time is never clamped:* clamping after a stall would silently slow
the line and break the accuracy requirement. Instead, per-frame *work* is capped
(`ParseBudgetBytes`, initially 256 KB) while unspent credit is retained. The
accounting stays exact; only the catch-up is spread over frames. At 115200 baud
the cap is 1300x the normal per-frame release, so it engages only after a stall
or during a fast-forward.

*Metering continues when the window is hidden or minimised.* The line does not
stop because you looked away, and the alternative (pause and burst on restore)
is both less authentic and a source of frame-time spikes.

*Testability, which is the real payoff:* the governor takes an `ITimeSource`. A
fake clock advanced by exact increments lets tests assert exact byte counts —
`advance(1s) at 1200 8N1 ⇒ exactly 120 bytes` — with no sleeps and no flakes. A
separate slow-category test runs the real clock for 60 s at 9600 baud and
asserts the ±0.5% window from the spec (57,312–57,888 bytes).

### D4. ConPTY by direct P/Invoke

`CreatePseudoConsole`, `ResizePseudoConsole`, `ClosePseudoConsole`, plus
`InitializeProcThreadAttributeList` / `UpdateProcThreadAttribute` with
`PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE` (0x00020016) and `CreateProcessW` with
`EXTENDED_STARTUPINFO_PRESENT`. Roughly 250 lines, no package dependency.

*Why not a wrapper package:* the difficulty in ConPTY hosting is not the API
surface, it is handle lifetime — the order in which the input write handle, the
output read handle, and the pseudoconsole itself are closed determines whether
the reader thread returns or hangs forever on shutdown. That ordering has to be
understood and owned regardless of whether a package wraps the calls, and
wrapping it makes the ordering harder to see. Keeping it as auditable interop
also keeps `Baudacious.Pty` dependency-free.

**Two traps, both measured in Phase 0, both of which fail silently:**

`dwFlags` must be **0**. `PSEUDOCONSOLE_INHERIT_CURSOR` makes ConPTY probe the
terminal for its cursor position (`ESC[6n`) at startup and withhold all output
until answered — and the hosted shell then hits end-of-input and exits with
status 0 about 100 ms in, after printing a banner and prompt. The flag only
carries a parent console's cursor across, which means nothing to an application
that owns its own window.

`dwCreationFlags` must be **`EXTENDED_STARTUPINFO_PRESENT` and nothing else**. Any
console-creation flag — `CREATE_NO_WINDOW`, `CREATE_NEW_CONSOLE`,
`DETACHED_PROCESS` — gives the child its own console so it never attaches to the
pseudoconsole, leaving the output pipe empty forever.

`ResizePseudoConsole` is the mechanism behind the grid-propagation requirement;
ConPTY's buffer width, not ours, is what determines where the shell wraps.

Baudacious does **not** need a console of its own: a console-less windowed process
hosts ConPTY correctly, and `AllocConsole` neither helps nor is required.

### D5. Hand-rolled VT parser on the DEC/Williams state machine

The canonical DEC ANSI-compatible parser state machine (ground, escape,
csi_entry, csi_param, csi_intermediate, csi_ignore, osc_string, dcs_*,
sos_pm_apc_string), implemented as an explicit table.

*Why hand-rolled:* the parser must hold its state across governor boundaries —
a CSI sequence routinely arrives with its bytes split across several frames, and
at 300 baud `ESC [ 3 1 m` spans five frames. A state machine gives this for
free, and the requirement that partial sequences never appear as text falls out
of the design rather than being bolted on. Beyond that, the grid model needs to
be ours anyway (phosphor luminance mapping, wrap-flag-based reflow,
fast-forward determinism), so an external parser would be feeding a translation
layer into our own model.

*Alternatives:* `libvterm` via P/Invoke (adds a native dependency and a C
ownership model to marshal), XtermSharp / vtnet (viable, but both assume they
own the grid, and neither is actively maintained).

*Test strategy:* replay captured real ConPTY byte streams and snapshot the
resulting grid. Then the invariant test that matters most — feed the same
stream byte-at-a-time and all-at-once and assert the grids are identical. That
single property test guards the entire governor/parser boundary.

### D6. Cell grid and scrollback

```
struct Cell {          // 16 bytes
    uint Rune;         // code point
    uint Fg;           // packed RGBA, resolved from the palette at write time
    uint Bg;
    ushort Flags;      // bold/faint/italic/underline/blink/reverse/invisible/strike
    ushort _pad;
}
```

A 200x60 grid is 12,000 cells ≈ 188 KB. No optimisation warranted.

Rows are stored with a `SoftWrapped` flag and a logical-line id. **This is not
derivable after the fact**, and it is the thing that makes reflow-on-resize
work and copy-of-a-wrapped-line produce one line instead of two. Reflow is the
classic terminal bug farm; carrying the flag from the first commit is the cheap
insurance.

Colors are resolved to RGBA at write time rather than stored as palette
indices, so a palette change does not retroactively recolor history. Phosphor
mapping (D10) happens at render time from the resolved RGBA, so switching
phosphor *does* affect history — which is the right behaviour for a display
property.

Scrollback is a ring buffer of rows, default 10,000 lines.

### D7. Two-pass rendering: native-resolution text, then post-process

```
Pass 1: instanced quads ──▶ FBO at native cell resolution (cols*8 x rows*16)
Pass 2: FBO ──▶ downsample ──▶ separable blur ──▶ composite (bloom + scanlines
                                                   + vignette) ──▶ screen
```

Pass 1 draws one instanced quad per cell from a CP437 glyph atlas: a single R8
texture, 16x16 glyphs of 8x16 pixels = 128x256 pixels. The per-frame instance
buffer is 16 bytes x 12,000 = 188 KB, or 11.3 MB/s at 60 Hz. Trivial; dirty-cell
tracking is available later if profiling ever asks for it.

*Why render to a native-resolution offscreen target first:* scanlines, bloom
radius, and phosphor bleed are properties of the *CRT's* pixel grid, not the
LCD's. Defining them in the offscreen target's space and upscaling in pass 2 is
what makes them look like a CRT rather than like a filter applied to a
screenshot. It is also what satisfies the requirement that effects are isolated
from text rasterisation — every future effect is a change to pass 2.

Total draw calls per frame: about five. Text rasterisation costs one.

*v1 effects:* phosphor bloom (downsample to 1/2 and 1/4, separable Gaussian,
additive composite), horizontal scanlines, edge vignette. Each with an
independent intensity uniform, and the whole pass bypassable with a direct blit.

*Explicitly deferred but pre-planned:* barrel/pincushion distortion (a UV
transform at the start of pass 2), aperture-grille and slot masks (a tiling
texture multiply), phosphor persistence and decay (needs one extra ping-pong
FBO retaining the previous frame — the only deferred effect that changes the
pass *structure*, so pass 2 is written taking its target as a parameter from the
start), chromatic aberration, and interlace flicker.

*Font asset:* the default is a CP437 IBM VGA 8x16 face. Two candidate sources —
VileR's Oldschool PC Font Pack, and generating the atlas at build time from a
public-domain 8x16 ROM-derived bitmap. The licence question is real but does not
affect any code: the atlas is one embedded asset behind one loader. See Open
Questions.

### D8. Four threads, one owner per data structure

| Thread | Responsibility | Blocking? |
|--------|----------------|-----------|
| PTY reader | `ReadFile` on the ConPTY output handle into pooled buffers; push to the ring | Yes, always |
| Main / render | Governor tick, parser, grid mutation, instance build, draw, present | vsync only |
| PTY writer | Drain an input queue; `WriteFile` to the ConPTY input handle | Yes, may block |
| Config watcher | `FileSystemWatcher`, debounce, parse, hand off | Idle |

The parser and grid run on the main thread and are touched by nothing else, so
the hot path needs no locking. The SPSC ring buffer is the only structure
crossing threads on the data path.

*As built (Phase 1):* `ByteRing` is a chain of recycled 8 KB segments. The byte
path is genuinely lock-free — one volatile append index the producer owns, one
volatile take index the consumer owns — but the segment chain and the recycling
pool are guarded by a lock, entered once per segment rather than once per write.
At 115200 baud that is about once every 0.7 seconds, so no bulk copy ever happens
under the lock and the render thread is never made to wait on one. A fully
lock-free free list was not worth the proof obligation for a lock taken that
rarely; revisit only if profiling says otherwise.

*Why a separate writer thread:* `WriteFile` to a pseudoconsole input handle can
block if the child is not reading. Doing it from a GLFW input callback would
stall the render loop, which would stall the governor, which would corrupt the
one thing this application exists to get right.

### D9. Fast-forward and flush use the same code path as normal metering

**Fast-forward (Alt+F):** grant unbounded credit for one tick, still subject to
`ParseBudgetBytes`, spilling into subsequent frames if the backlog is enormous.
Bytes flow through the same governor and the same parser. There is deliberately
no separate "fast path", because a second path is how the fast and slow routes
end up disagreeing about the final screen state.

**Held 10x skim (Alt+Shift+F):** multiply the rate term while the key is down.
One line of change to the credit calculation.

**Flush-on-interrupt (Ctrl+C):** discard the ring buffer contents *and* reset
the parser to the ground state. The reset is mandatory and easy to forget:
dropping bytes mid-sequence would otherwise leave the parser waiting for a final
byte that will never arrive, swallowing everything after it. Note the asymmetry
— fast-forward must **not** reset the parser (no bytes are lost), flush must.
The current SGR attribute state is preserved across a flush rather than reset,
so the screen does not change color because you pressed Ctrl+C.

### D10. Phosphor mapping is a render-time transform

Monochrome palettes map each cell's resolved RGBA to a luminance
(Rec. 709: `0.2126R + 0.7152G + 0.0722B`), then to a phosphor ramp. Bold adds an
intensity boost; reverse video swaps foreground and background luminance.
Starting values, all tunable: P1 green `#33FF66`, P3 amber `#FFB000`, P4 white
`#F0F0FF`.

*Why luminance rather than a per-color lookup:* it gives a defensible answer
for 24-bit color, which a 16-entry table cannot, and it degrades sensibly for
color schemes the author never saw.

Full-color mode skips the transform. It is not the aesthetic point of the
product, but a monochrome-only terminal is one that cannot show you a `git diff`.

### D11. INI configuration, hand-rolled, with state kept separately

`%APPDATA%\Baudacious\baudacious.ini` for user settings; `state.ini` alongside it for
persisted runtime state.

*Why INI:* comments survive round-tripping (which JSON cannot do and the spec
requires), it is trivially hand-editable, sections give shell profiles a natural
shape (`[shell.pwsh]`), a reader is about 150 lines, and it adds no dependency.
It is also exactly the format ProComm Plus and Telix used, which is not a
technical argument but is not nothing either.

*Alternatives:* TOML via Tomlyn (better typed, adds a dependency for a config
file with about thirty scalar keys), JSON (no comments; JSONC is nonstandard),
YAML (significant-whitespace failure modes in a file users hand-edit).

*Why two files:* the spec requires that runtime state never rewrites the user's
commented file. Two files makes that structural rather than a promise about
serialiser fidelity. Precedence: `state.ini` overrides `baudacious.ini` for the
keys it carries, and is absent on first run.

### D12. Project layout

| Project | Contains | Depends on |
|---------|----------|-----------|
| `Baudacious.Core` | Governor, `ITimeSource`, VT parser, cell grid, scrollback, palettes | nothing |
| `Baudacious.Pty` | ConPTY interop, session lifetime, shell profiles | nothing |
| `Baudacious.Render` | GL device, glyph atlas, instance batching, shaders (embedded) | Silk.NET |
| `Baudacious.App` | Composition root, window, input mapping, local commands, config | all of the above |
| `Baudacious.Core.Tests` | Deterministic unit and property tests, fake clock | Core |
| `Baudacious.Pty.Tests` | Integration tests, requires Windows and a real pwsh | Pty |

`Baudacious.Core` referencing nothing is the point of the split: the parts that
have to be *correct* are testable without a GPU, a shell, or a clock.

## Risks / Trade-offs

**~~ConPTY repaint chattiness~~ — RESOLVED in Phase 0.** Measured benign: ConPTY
sends exactly zero bytes while sitting idle at a prompt, on all three profiles.
There is nothing to coalesce, and D2 needs no escape hatch.

**~~PowerShell unusable for typing at low rates~~ — SOLVED in Phase 0.** The
rich line editor, not the shell, was the cost. Removing PSReadLine puts pwsh at
1.0 bytes per keystroke, exact parity with cmd.exe, usable down to 300 baud. Now
a v1 feature (`specs/shell-hosting`, "fast echo"), not a documented limitation.

**Residual risk: fast echo costs real features** → syntax coloring, inline
prediction, `Ctrl+R` history search and multi-line editing. Mitigated by making it
a mode rather than a fate: default below 4800 baud, toggleable on a live session,
always announced. **Tab completion turned out to survive** — verified by hand in
Phase 5, see phase-0-findings.md section 4a — because the console host's own line
reader calls `TabExpansion2` too. The 4800-baud threshold stays anyway: the three
features that really are lost are the ones someone above the threshold has the
wire to afford.

**Client-side line editing is no longer the only answer** → previously the planned
post-v1 fix. Still worth having eventually for shells with no cooked-input
fallback, but it is no longer on the critical path, and it has a problem fast echo
does not: with ConPTY there is no way to stop the shell echoing, so a locally
echoed character would be drawn twice.

**CRT post-processing degrading legibility** → Default intensities tuned
conservatively, `Alt+C` toggles the whole pass, and the CP437 glyphs that differ
only in fine detail become the acceptance test. An aesthetic that makes the
terminal worse to use has failed.

**Reflow-on-resize correctness** → The historically bug-prone area. Wrap flags
in the row model from the first commit, plus property tests that resize narrow →
wide → narrow and assert the content round-trips.

**~~Font licensing~~ — RESOLVED in Phase 3.** unscii-16, public domain, embedded. No
obligations to carry.

**Six capabilities is a lot for a v1** → Addressed by the phasing below, in
which each phase ends in something demonstrable rather than in a layer. The
timing core is proven in Phase 1, well before the GPU work that dominates the
calendar.

**Self-contained binary size** → 30–70 MB with the .NET runtime and GLFW native
library. Unremarkable for a desktop app; noted so it is not a surprise.

## Migration Plan

Greenfield, so no data or user migration. The rollout that matters is the
sequencing, and specifically that one measurement happens before the
architecture is committed.

**Phase 0 — Measure ConPTY (throwaway spike).** A console program that hosts
`pwsh` and `cmd.exe` via ConPTY and logs every byte received with a
microsecond timestamp. Answers: how many bytes does one keystroke cost? How
large is a default prompt redraw? Does ConPTY emit unsolicited repaints at
rest? These numbers decide whether D2 needs a coalescing escape hatch. The code
is discarded; the captured streams become parser test fixtures.

**Phase 1 — The governor, proven, with no GUI.** `Baudacious.Core` governor plus
`ITimeSource`, driven from a console harness that meters a file to stdout.
Deterministic tests and the ±0.5% stopwatch test both pass here. The product's
central claim is demonstrable before a single line of OpenGL exists.

**Phase 2 — Emulation.** VT parser and cell grid, fed by Phase 0's captured
streams. Snapshot tests, and the byte-at-a-time-equals-all-at-once property
test. Still no window; the grid renders to text for verification.

**Phase 3 — First light.** Window, glyph atlas, instanced text, native-resolution
FBO with a straight blit. Wire ConPTY, governor, parser, and grid together.
This is the phase that produces a usable terminal with correct baud timing and
crisp text.

**Phase 4 — Input and local commands.** Key encoding, Alt chords, `Ctrl+]`
prompt, selection, clipboard, fast-forward, flush.

**Phase 5 — The CRT pass.** Bloom, scanlines, vignette, phosphor palettes,
intensity controls. Purely additive to pass 2; nothing before it changes.

**Phase 6 — Configuration and polish.** INI reader, hot reload, state
persistence, help overlay, shell profiles, backlog indicator.

Rollback is per-phase: Phases 4–6 are independently droppable from a v1 release,
and Phase 5 in particular can ship dark (effects present, disabled by default)
if it does not meet the legibility bar in time.

## Open Questions

- ~~**Font asset provenance.**~~ **RESOLVED (Phase 3).** unscii-16 by Viznut, an 8x16
  bitmap face in the **public domain**, embedded as a `.hex` resource. It covers 250 of
  the 256 CP437 slots; two of the rest are derived from glyphs it does have and two fall
  back to a thresholded system face. No attribution obligation, no share-alike, nothing to
  reconcile if Baudacious is licensed permissively later. Note the trap: the `unscii-16-full`
  variant pulls in GNU Unifont and is GPL — only the plain variant is public domain.
  VileR's CC BY-SA pack was the alternative and was passed over to keep the asset licence
  clean. See `assets/README.md`.

- **Exact CRT parameter values.** Bloom radius, scanline duty cycle and depth,
  vignette falloff. These are tuned by eye against reference photographs in
  Phase 5, not decided now.
- ~~**Whether to expose CRT presets**~~ **DECIDED (Phase 5): no.** Now that the three
  intensities exist and can be judged, naming combinations of them adds vocabulary without
  adding capability — and every named preset is a thing to document, translate and keep
  honest. The defaults are the one combination worth shipping, and `--no-crt` is the
  purist option. Reconsider only if a fourth or fifth effect arrives and the space of
  useful settings stops being obvious.

- ~~**Whether the backlog indicator should show a progress bar or just a marker.**~~
  **DECIDED (Phase 6): a count and a time, no bar.** The arithmetic settles it. A bar
  needs a denominator, and the denominator is still growing — output arrives while the
  queue drains, so the bar would slide backwards exactly when someone is waiting on it.
  `[881 pending, 29s]` is stable and answers the question they actually have. Long waits
  are quoted in minutes, because at 110 baud a pasted paragraph really is minutes away.
  Unlimited and fast-forward say `draining` rather than quoting a wait of zero.
