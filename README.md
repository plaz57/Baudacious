# Baudacious

A terminal emulator with a baud rate.

Text that arrives at 300 baud is *read* as it arrives. Text at 1200 baud has a
cadence. A directory listing at 2400 baud is an event with a beginning and an
end. No current terminal reproduces this, and the ones that try do it by
sleeping between whole lines — which is exactly wrong, because the character is
the unit, not the line.

Baudacious meters its output onto the screen at a real, user-selected line rate,
accurately enough that a stopwatch agrees with it.

## Try it

Needs .NET 10, Windows 10 1809 or later, and a GPU with OpenGL 3.3.

```
dotnet run -c Release --project src/Baudacious.App -- --baud 1200 --shell cmd
```

Three shells: `--shell pwsh` (default), `cmd`, or `wsl`. Name a distribution with
`--shell wsl:Ubuntu`, or leave it off and WSL picks its own default.

| Key | |
|---|---|
| `Alt+B` | step through 110 → 300 → 1200 → … → 115200 |
| `Alt+F` | fast-forward the queued backlog |
| `Alt+P` | cycle phosphor: green, amber, white, full color |
| `Alt+C` | toggle the CRT pass |
| `Alt+-` / `Alt+=` | font scale |
| `Alt+X` | quit |
| `Ctrl+C` | interrupt the shell **and** drop the backlog |

Try `dir /s` at 1200 baud, then `Alt+F` when you have seen enough. Add
`--phosphor amber` for the other kind of monitor.

There are three diagnostics, all of which want a **real terminal window** —
piping their output confuses the hosted shell's console attachment:

```
... console --baud 1200 --shell cmd        the same thing, painted into the host console
... meter tests/fixtures/conpty-captures/cmd-listing.bin --baud 300
... shot --out frame.png                   render one frame through the real GL pipeline
```

## How it works

```
ConPTY ──▶ ByteRing ──▶ BaudGovernor ──▶ VtParser ──▶ Screen ──▶ renderer
 (shell)   (unbounded)   (per frame)      (per byte)   (grid)
```

Three decisions carry the design.

**Metering happens on raw bytes, ahead of escape-sequence interpretation.** A
UART has no idea what ANSI is, so `ESC[31m` costs five characters of wire time
exactly as five letters would. Measured across real sessions, escape sequences
are 28–48% of the interactive byte stream, so metering decoded glyphs instead
would deliver a session at roughly twice the speed a real line ever did — and
flatten precisely the uneven cadence the product exists to reproduce.

**Timing is a credit accumulator against a monotonic clock, never a sleep and
never a frame count.** At 300 baud a 60 Hz frame earns half a byte, so counting
frames would be wrong by construction. Credit is held as an exact integer
fraction rather than a `double`: accumulated in floating point, the last byte of
every second is lost to a ~1e-13 shortfall, and 300 baud delivers 29 bytes per
second instead of 30.

**The shell is never throttled.** It runs at full speed and its output queues; a
command that finishes in 20 ms may still be painting thirty seconds later, which
is the authentic behaviour of a fast host on a slow line.

## Status

Phases 0–3 and 5 of [the v1 plan](openspec/changes/baudacious-v1/tasks.md) are
done: ConPTY hosting, the governor, terminal emulation, the GPU window and the
CRT pass. Phase 4 — the full input and local-command layer — is the remaining
gap; the window has the bindings it needs but not the local command prompt,
selection or clipboard.

- `src/Baudacious.Core` — governor, VT parser, cell grid, scrollback, reflow.
  No I/O, no graphics, no clock of its own; fully unit-testable.
- `src/Baudacious.Pty` — ConPTY interop. Hand-written, because the hard part of
  ConPTY is handle lifetime and wrapping it hides the thing that has to be right.
- `src/Baudacious.Render` — OpenGL 3.3. Text goes into an offscreen target at
  native cell resolution; the CRT pass then composites bloom, scanlines and
  vignette at output resolution while taking its scanline period from the source
  pixel grid. At a 200x60 grid the whole frame costs 0.97 ms against a 16.67 ms
  budget, and `--no-crt` brings that to 0.33 ms.
- `src/Baudacious.App` — composition root, the window, and the diagnostics.
- `spike/` — the Phase 0 measurement spike. Throwaway, kept for the record.

185 tests. The one that matters most replays every recorded ConPTY session twice
— one byte at a time, and all at once — and requires an identical grid, cursor
and scrollback. Without that invariant the baud rate would change *what* you see
rather than only *when*, and every emulation bug would become rate-dependent.

```
dotnet test
BAUDACIOUS_CONFORMANCE_SECONDS=60 dotnet test --filter Category=Timing
```

## Notes from the measurements

Recorded in [phase-0-findings.md](openspec/changes/baudacious-v1/phase-0-findings.md):

- **ConPTY is silent at rest** — zero bytes over five seconds at an idle prompt.
- **`cmd.exe` and WSL's bash both echo 1.0 bytes per keystroke** and are
  comfortable at every rate, including 300 baud. bash's readline repaints the
  input line only for history and completion, not for ordinary typing.
- **PowerShell echoes 96.9** by default, because PSReadLine repaints the whole
  input line on every keystroke. Tuning its options gets that to ~28; *removing*
  PSReadLine gets it to exactly 1.0, matching cmd, because PowerShell then falls
  back to the console's cooked line input. That is the `shell` command's default
  for pwsh; pass `--rich-editor` to feel the difference at 1200 baud.
- **`PSEUDOCONSOLE_INHERIT_CURSOR` must not be set.** With it, ConPTY withholds
  all output pending a cursor-position report, and the hosted shell then reaches
  end-of-input and exits with status 0 about 100 ms in — after printing a banner
  and a prompt, so it looks like it worked.

## The CRT pass

Three effects over the text, each with its own intensity, all switchable with `Alt+C`.

| Flag | Default | What it does |
|---|---|---|
| `--bloom` | `0.55` | Phosphor bleed. **Additive and unbounded**, so values above 1 keep going. Its effect is almost entirely on the black around glyphs — the glyph cores are already near maximum. At 2.5 the halo spreads far enough to roughly triple how much of the screen reads as lit. |
| `--scanline` | `0.50` | Darkens the second half of every source pixel row, so the light removed averages `scanline x 0.5`. At 1.0 the gaps go fully black. |
| `--vignette` | `0.35` | Corner falloff. Untouched through the middle 60% of the width; darkening starts in the outer 20% each side and the outer 15% top and bottom. At the extreme corner the darkening is about `vignette x 0.82`. |

Both `scanline` and `vignette` are fractions of brightness *removed*, so 0–1 is the whole
useful range. Bloom is not — it adds light, and washes out somewhere past 2.

**Scanlines need `--scale 2` or more.** At scale 1 there is one output pixel per source
pixel row, so every row samples the same scanline phase: the effect degrades to a slight
uniform dimming with no banding at all.

A fourth knob is not on the command line: a small ambient glow (0.02) keeps unlit phosphor
from being pure black, which is why measured luminances sit a little above the arithmetic.

Render stills without starting a session, which is how these numbers were measured:

```
... shot --vignette 0.7 --bloom 0.9 --scanline 0.45 --magnify 3 --out frame.png
... shot --bench --size 200x60 --magnify 2
```

## Scale

`--scale` is how many screen pixels go to one glyph pixel. The atlas cell is 8x16, so
`--scale 3` draws each character in a 24x48 block. It sets the initial window size to fit
80x25, and thereafter decides how much text fits rather than how big the window is:

```
columns = windowWidth  / (8 x scale)
rows    = windowHeight / (16 x scale) - 1      the spare row is the status bar
```

Bigger scale means bigger characters and fewer of them — at 1280 wide, scale 2 gives 80
columns, scale 3 gives 53. `Alt+-` and `Alt+=` change it live.

Always a whole number: a fractional scale resamples glyph pixels unevenly, making some
stems a pixel wider than others, and would put the scanline period out of step with the
pixel grid it is meant to describe. Leftover pixels become a centred margin.

## Fonts

Text is drawn from **unscii-16** by Viznut, an 8x16 bitmap face in the **public domain**,
embedded as a `.hex` resource. A bitmap font rather than an outline one because an outline
typeface rasterised to eight pixels wide has no room to make good decisions and comes out
mushy — and because a face designed for this cell draws box-drawing characters that
actually join. See [assets/README.md](assets/README.md).

## Licence

[MIT](LICENSE). Do what you like with it.

Everything Baudacious depends on is permissive too — Silk.NET and System.Drawing.Common
are MIT, xunit is Apache-2.0 and test-only, and the bundled font is public domain. Nothing
in the tree is copyleft. See [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
