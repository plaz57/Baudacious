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

Needs Windows 10 1809 or later and a GPU with OpenGL 3.3. From source, needs
.NET 10 as well:

```
dotnet run -c Release --project src/Baudacious.App -- --baud 1200 --shell cmd
```

Or build something to keep. `baudacious.exe` is self-contained — no runtime to
install — and `glfw3.dll` has to travel beside it:

```
dotnet publish src/Baudacious.App -c Release -p:PublishSingleFile=true -o dist
```

`Baudacious.sln` opens the four libraries, both test projects and the ConPTY
spike in one go, with the settings files and the specs as solution items.

Three shells: `--shell pwsh` (default), `cmd`, or `wsl`. Name a distribution with
`--shell wsl:Ubuntu`, or leave it off and WSL picks its own default. More can be
added in the settings file.

Out of the box: **4800 baud 8N1**, green phosphor, unscii at 2x in a 100x30
window, and the CRT pass on with a strong bloom, deep corners and a long
phosphor tail. Curvature and the aperture grille are off. Every one of those is
a `--flag`, a line in the settings file, and a key or a prompt command — and
whatever you leave it set to is what it opens with next time.

| Key | |
|---|---|
| `Alt+B` | line rate menu — 110 through 115200, or unlimited |
| `Alt+G` | framing menu — 8N1, 7E1, 8N2, 8E1 |
| `Alt+P` | phosphor menu — green, amber, white, full color |
| `Alt+F` | release the queued backlog at once |
| `Alt+Shift+F` | ten times the rate, while held |
| `Alt+C` | CRT pass on or off |
| `Alt+N` / `Alt+S` | another shell / show the next one |
| `Alt+-` / `Alt+=` | font scale |
| `Alt+Enter` | fullscreen |
| `Alt+Z` | every binding, and where the settings live |
| `Alt+X` | quit — twice, within three seconds |
| `Ctrl+]` | the local command prompt |
| `Ctrl+C` | interrupt the shell **and** drop the backlog |

The three menu keys open a list with the current setting marked; pressing the
chord again steps down it, and the choice takes effect as the highlight moves.
Every binding is configurable.

Try `dir /s` at 1200 baud, then `Alt+F` when you have seen enough. Add
`--phosphor amber` for the other kind of monitor.

### The local command prompt

`Ctrl+]` opens a prompt that is Baudacious's own — nothing typed at it reaches
the shell. Tab completes in every position, up and down walk the history, and
any unambiguous abbreviation works, so `b 2400` is `baud 2400`.

| | |
|---|---|
| `baud <rate>` | 110 to 115200, or `unlimited`. `baud 1200 8N2` sets framing too |
| `framing <spec>` | `8N1`, `7E1`, `8N2`, `8E1` |
| `flush` | release everything still queued |
| `phosphor <name>` | `green`, `amber`, `white`, `color` |
| `font <name>` | `unscii`, `terminus`, `spleen` |
| `fontsize <1-8>` | magnification of the 8x16 cell |
| `crt [on\|off]` | the CRT pass; bare toggles |
| `crt <effect> <0-1>` | one effect: `bloom` `scanline` `vignette` `curvature` `mask` `persistence`. Tab lists them. Naming one turns the pass on |
| `fastecho [on\|off]` | swap PowerShell's line editor for the console's |
| `session new <profile>` | another shell |
| `about` | version, licence, and whose typefaces these are |
| `help` | the list above |
| `quit` | close Baudacious |

## Settings

`%APPDATA%\Baudacious\baudacious.ini` is written with commented defaults on
first run and re-read within a second of being saved — the palette, the CRT
intensities, the bindings and the typeface all change under a running session
without disturbing the shells. `--settings <directory>` reads from somewhere
else instead.

Alongside it, `state.ini` records what the last session was left set to, so
Baudacious comes back the way it was closed. Baudacious writes only that file;
`baudacious.ini` is yours and is never rewritten after it is created.

| Section | Setting | |
|---|---|---|
| `[line]` | `baud` | 110 to 115200, or `unlimited`. Any number in range, standard or not |
| | `framing` | Bits per character: `8N1`, `7E1` (ten bits), `8N2`, `8E1` (eleven) |
| `[shell]` | `default` | `pwsh`, `cmd`, `wsl`, `wsl:<distribution>`, or a profile below |
| | `fast-echo` | `auto`, `on`, `off` — see below |
| | `powershell-profile` | Whether pwsh runs your own `$PROFILE`, and so whether your aliases and functions exist. On. Off saves the wire time a banner or themed prompt costs at low rates |
| `[display]` | `phosphor` | `green`, `amber`, `white`, `color` |
| | `font` | `unscii`, `terminus`, `spleen` |
| | `font-scale` | 1 to 8, whole numbers only |
| | `cursor` | `block`, `underline`, `bar` |
| | `cursor-blink` | Milliseconds; `0` for a steady cursor |
| | `scrollback` | Lines kept per session |
| `[crt]` | `enabled` | The whole pass |
| | `bloom` `scanline` `vignette` `curvature` `mask` `persistence` `ambient-glow` | Intensities: 0 to 1, except `bloom`, which is additive and runs to 4 — see [the CRT pass](#the-crt-pass) |
| `[window]` | `columns` `rows` | Opening size in cells, not pixels |
| | `fullscreen` | |
| `[keys]` | `line-rate` `framing` `fast-forward` `skim` `phosphor` `crt` `scale-up` `scale-down` `fullscreen` `new-session` `next-session` `commands` `help` `quit` | A chord such as `alt+B`, `alt+shift+F` or `ctrl+]`, or `none` to unbind |

Extra shells get a section of their own. `like = pwsh` borrows PowerShell's
fast-echo handling for another PowerShell; leave it out for anything else.

```ini
[profile.gitbash]
command = C:\Program Files\Git\bin\bash.exe
arguments = --login -i
```

A value that will not parse costs you that value and not the file: the rest is
honoured, the default stands in, and the reason appears on screen naming the
setting and its line number. Deleting a line withdraws an opinion rather than
issuing one — the last session's setting applies, or the default if there is
none. Changing a line takes that setting back from the persisted state for the
rest of the run, which is what makes editing the palette while running actually
change the palette.

Anything on the command line wins over both files, for that run only.

### Fast echo

Per-keystroke echo cost is the shell's line editor, not the shell, and it varies
by two orders of magnitude. `cmd.exe` and WSL's bash echo **1.0 bytes per
keypress** and are comfortable at 300 baud. Stock PowerShell echoes **96.9**,
because PSReadLine repaints the whole input line on every keystroke; tuning its
options gets that to about 28, and *removing* PSReadLine gets it to exactly 1.0,
because PowerShell then falls back to the console's cooked line input.

So `fast-echo = auto` removes PSReadLine below 4800 baud and keeps it above.
What that costs is syntax coloring, inline prediction and `Ctrl+R` history
search — **not** tab completion, which the console host provides itself.
`fastecho on|off` at the local prompt changes it on a running shell without
losing its history or working directory.

Below the threshold with the rich editor, Baudacious says so once and gives the
arithmetic: at 1200 baud, 28.5 bytes a keypress at four keypresses a second is
0.95 seconds of wire for every second of typing. The point is that the cost
accumulates — the backlog grows for as long as you keep typing rather than
settling at a fixed delay.

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
is the authentic behaviour of a fast host on a slow line. Every session in a
window is metered every frame, foreground or not — a wire does not stop carrying
because nobody is watching.

## Status

All of [the v1 plan](openspec/changes/baudacious-v1/tasks.md) is implemented
except its final manual acceptance pass.

- `src/Baudacious.Core` — governor, VT parser, cell grid, scrollback, reflow,
  the local command language, INI reading. No I/O, no graphics, no clock of its
  own; fully unit-testable.
- `src/Baudacious.Pty` — ConPTY interop. Hand-written, because the hard part of
  ConPTY is handle lifetime and wrapping it hides the thing that has to be right.
- `src/Baudacious.Render` — OpenGL 3.3. Text goes into an offscreen target at
  native cell resolution; the CRT pass then composites bloom, scanlines and
  vignette at output resolution while taking its scanline period from the source
  pixel grid. At a 200x60 grid the whole frame costs 0.97 ms against a 16.67 ms
  budget, and `--no-crt` brings that to 0.33 ms.
- `src/Baudacious.App` — composition root, the window, the settings, and the
  diagnostics.
- `spike/` — the Phase 0 measurement spike. Throwaway, kept for the record.

436 tests. The one that matters most replays every recorded ConPTY session twice
— one byte at a time, and all at once — and requires an identical grid, cursor
and scrollback. Without that invariant the baud rate would change *what* you see
rather than only *when*, and every emulation bug would become rate-dependent.

```
dotnet test
BAUDACIOUS_CONFORMANCE_SECONDS=60 dotnet test --filter Category=Timing
```

### Diagnostics

Four subcommands, and the first two want a **real terminal window** — piping
their output confuses the hosted shell's console attachment:

```
baudacious console --baud 1200 --shell cmd     the same thing, in the host console
baudacious meter tests/fixtures/conpty-captures/cmd-listing.bin --baud 300
baudacious shot --out frame.png                one frame through the real GL pipeline
baudacious atlas atlas.png 4                   dump the glyph atlas
```

## Notes from the measurements

Recorded in [phase-0-findings.md](openspec/changes/baudacious-v1/phase-0-findings.md):

- **ConPTY is silent at rest** — zero bytes over five seconds at an idle prompt.
- **`cmd.exe` and WSL's bash both echo 1.0 bytes per keystroke** and are
  comfortable at every rate, including 300 baud. bash's readline repaints the
  input line only for history and completion, not for ordinary typing.
- **Tab completion survives fast echo.** Phase 0 guessed it would not, on the
  reasoning that PSReadLine provides it, and recorded the question as unverified
  because the test written to settle it was faulty. It was verified by hand in
  Phase 6: command, path and parameter completion all work with the module
  removed, because the console host's own line reader calls `TabExpansion2`.
- **`PSEUDOCONSOLE_INHERIT_CURSOR` must not be set.** With it, ConPTY withholds
  all output pending a cursor-position report, and the hosted shell then reaches
  end-of-input and exits with status 0 about 100 ms in — after printing a banner
  and a prompt, so it looks like it worked.

## The CRT pass

Six effects over the text, each with its own intensity, all switchable together with
`Alt+C`. Everything here is also a `--flag` on the command line, and `--no-crt` skips the
pass entirely.

| Setting | Default | What it does |
|---|---|---|
| `bloom` | `0.90` | Phosphor bleed. **Additive**, so values above 1 keep going rather than saturating; capped at 4. Its effect is almost entirely on the black around glyphs — the glyph cores are already near maximum. At 2.5 the halo spreads far enough to roughly triple how much of the screen reads as lit. |
| `scanline` | `0.50` | Darkens the second half of every source pixel row, so the light removed averages `scanline x 0.5`. At 1.0 the gaps go fully black. Follows the curvature, because raster lines were on the phosphor rather than over it. |
| `vignette` | `0.70` | Corner falloff. Untouched through the middle 60% of the width; darkening starts in the outer 20% each side and the outer 15% top and bottom. At the extreme corner the darkening is about `vignette x 0.82`. |
| `curvature` | `0` | The glass bulges toward you, so straight rows of text bow outward in the middle. Past the edge of the glass the pixels are unlit phosphor, not a smear of the nearest one. At 1.0 it reads as a fisheye and the corners go dark. **The status bar is never curved** — it is chrome, and the one line that has to stay legible. Off by default; `0.40` is the value to try first. |
| `persistence` | `0.90` | How long light lingers after the pixel that made it has gone. The tail is gone by frame 4 at `0.45`, frame 8 at `0.6` and frame 32 at `1.0`, so the default is a fade of roughly twenty frames — about a third of a second. |
| `mask` | `0` | Aperture-grille stripes: output pixels weighted into repeating red, green and blue columns, phase fixed to the output pixel so the pattern cannot swim when the grid resizes. |

`scanline`, `vignette`, `curvature`, `persistence` and `mask` all run 0–1 — they scale
something that already exists and are meaningless past it. Bloom runs 0–4, because it adds
light rather than scaling it; it washes the screen out somewhere past 2. Values outside a
range are refused with the range named, not clamped.

Two of these defaults were moved after a day of using the thing rather than looking at it.
Curvature shipped at `0.40` — obvious at a glance, deforms no glyph — and went to zero,
because a bowed row of text is charming for a minute and then it is a bowed row of text you
are trying to read. Persistence shipped at `0.45` and went up to `0.90`, which is the right
direction for the one effect here that renders something a tube did and a flat panel
cannot. A preference that survives use beats one that survives a screenshot.

**Persistence acts on light going away**, not on light arriving. What it renders is the
fading of everything that went dark: a cleared screen, a scrolled one, the blinking cursor,
text overwritten in place. A pixel that stays lit shows nothing, and one that has just been
lit is at full brightness — so no trail follows text as it is written, however slowly it
arrives. That was the theory this was built on and watching it live disproved it; the cell
the cursor leaves has the new glyph drawn into it in the same frame, so its ghost is hidden
under the glyph.

The dial maps to 0.9 survival a frame at the top rather than straight through, because 1.0
meaning "never fades" is a bug however it is spelled. Measured by presenting blank frames
through the pass and reading the luminance back, a cleared screen reaches unlit by frame 4
at the default, frame 8 at 0.6 and frame 32 at 1.0.

**The mask is off by default, and deliberately.** An aperture grille is a colour-tube
artifact: a P1 green monochrome tube had a continuous phosphor coating and no grille at
all, so switching it on under the monochrome palettes is a stylistic choice rather than a
more faithful one. It comes into its own with `phosphor = color`, where 0.5 and up reads
like a Trinitron under a loupe. Masking costs light by construction — two stripes in three
are dimmed — so the weights are normalised to hold the level: measured mean luminance moves
1.2% at 0.25 and 4.2% at 0.5, and past that the compensation runs into the top of the range
and cannot recover what clipping takes.

**Scanlines and the mask both want a font scale of 2 or more, for opposite reasons.**
Scanlines need it to exist at all: at scale 1 there is one output pixel per source pixel
row, so every row samples the same phase and the effect degrades to uniform dimming. The
mask needs it to stay out of the way: at scale 1 a character is eight pixels wide against a
three-pixel triad, and the mask breaks the glyphs up rather than texturing them.

A seventh knob is settings-only: a small ambient glow (0.02) keeps unlit phosphor from being
pure black, which is why measured luminances sit a little above the arithmetic.

All six together cost **0.09 ms a frame** — 1.06 ms against a 16.67 ms budget at a 200x60
grid, magnified twice.

Render stills without starting a session, which is how these numbers were measured.
`--fade` presents blank frames through the same pass afterwards, which is the only way to
see an effect that exists across frames rather than within one:

```
baudacious shot --curvature 0.7 --mask 0.6 --magnify 3 --status --out frame.png
baudacious shot --persistence 0.8 --fade 6 --out tail.png
baudacious shot --bench --size 200x60 --magnify 2
```

## Font scale

Font scale is how many screen pixels go to one glyph pixel. The atlas cell is 8x16, so a
scale of 3 draws each character in a 24x48 block. It decides how much text fits rather than
how big the window is:

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

Three 8x16 bitmap faces ship with it, chosen with `font` in the settings file or
`font <name>` at the local prompt, and applied between two frames.

| | Stems | |
|---|---|---|
| `unscii` | 2px | unscii-16 by Viznut, public domain |
| `terminus` | **1px** | `ter-u16n` from Terminus Font by Dimitar Toshkov Zhekov, OFL 1.1 |
| `spleen` | 2px | Spleen 8x16 by Frederic Cambus, BSD 2-Clause |

Stem width is the axis that matters: two pixels doubles again at 2x magnification, and four
pixels of stem is what "too bold" looks like at this size. Terminus is the light one.
Spleen is the same weight as unscii with rounder, wider letterforms — an alternative rather
than a lighter option.

Bitmap faces rather than outline ones because an outline typeface rasterised to eight pixels
wide has no room to make good decisions and comes out mushy — and because a face designed
for this cell draws box-drawing characters that actually join. Provenance, licences and the
BDF conversion are in [assets/README.md](assets/README.md).

## Security

Baudacious launches the shell you name, reads and writes two INI files under
`%APPDATA%\Baudacious`, and touches the clipboard when you copy or paste. That is the whole
list; there is no network access and nothing phones home. A full review of the source,
including the terminal-specific attack surface and the hardening items still open, is in
[SECURITY-REVIEW.md](SECURITY-REVIEW.md).

## Licence

[MIT](LICENSE). Do what you like with it.

Everything Baudacious depends on is permissive — Silk.NET and System.Drawing.Common are
MIT, xunit is Apache-2.0 and test-only. Of the bundled fonts, unscii is public domain,
Spleen is BSD-2 and Terminus is OFL 1.1; the OFL keeps that file under the OFL and does not
reach the application. Nothing in the tree is copyleft. See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
