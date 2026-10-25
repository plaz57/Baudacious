## 1. The animator

- [x] 1.1 Add `SmoothScroll` to `Baudacious.Render`: a GL-free class holding a
      pixel debt, advanced by `Scrolled(int lines, int cellHeight)` and drained
      by `Advance(TimeSpan delta, double linesPerSecond, int cellHeight)`,
      exposing `Offset` in source pixels and `IsAnimating`.
- [x] 1.2 Clamp the debt to one cell. It is both the honest failure described in
      the proposal and a hard rendering constraint: one overscan row is one row,
      so a debt of two lines has nothing to draw in the gap.
- [x] 1.3 A rate of zero, a zero cell height or a non-advancing clock SHALL
      leave `Offset` at zero, so the disabled path is the jump scroll exactly.
- [x] 1.4 Unit tests: one line drains over the expected number of frames; a
      burst of ten lines never exceeds one cell of debt; rate zero never
      animates; the offset reaches exactly zero rather than a residue.

## 2. Drawing the offset

- [x] 2.1 Add `DrawFlags.Fixed` and a matching `FLAG_FIXED` constant, and a
      `uScrollOffset` uniform to the text vertex shader that shifts a cell up by
      that many source pixels unless the cell is flagged fixed.
- [x] 2.2 Flag the overlay and the status row fixed. They are chrome: the status
      bar must not move, and a help panel sliding under its own text is worse
      than no animation at all.
- [x] 2.3 When the offset is non-zero, emit grid row `i` at cell row `i + 1` and
      the row above the viewport — `Screen.TryGetAbsoluteRow(AbsoluteRowOf(0) - 1)`
      — at cell row 0. The last grid row then lands in the status bar's band and
      is overdrawn by it, which is what keeps the gap lit.
- [x] 2.4 Confirm the instance buffer already has room for the extra row;
      grow `needed` if not.
- [x] 2.5 Leave `ResizeTarget`, `Present`, the blit path, `CrtPass` and
      `ReadTarget` untouched. If any of them needs changing, the approach is
      wrong and worth revisiting before going further.
- [x] 2.6 Put the inversion behind `SmoothScroll.LiftFor`. The grid is emitted a
      whole row low and the lift cancels that row as the scroll lands, so the
      lift is the complement of the debt and not the debt. Written the other way
      round first, which ran the scroll downwards and ended it with a jump.

## 3. Emulator support

- [x] 3.1 Handle private mode 4 (DECSCLM) in `TerminalEmulator`, set and reset,
      exposing it as a requested-mode flag. Take care not to confuse it with the
      SGR `4` that means underline.
- [x] 3.2 Restore the mode to its configured state on DECSTR and RIS.
- [x] 3.3 Tests: `CSI ? 4 h` sets it, `CSI ? 4 l` clears it, `CSI 4 m` still
      means underline and does not touch it.

## 4. Wiring

- [x] 4.1 Track `Screen.ScrolledLines` per session in `TerminalWindow` and feed
      the delta to the animator each frame.
- [x] 4.2 Do not animate while scrolled back: moving through scrollback is
      navigation, not a scroll.
- [x] 4.3 Keep presenting while an animation is in progress, and keep the idle
      throttle from sleeping through it.
- [x] 4.4 Reset the animator on session switch, resize and font-scale change, so
      a stale offset cannot survive into a differently shaped grid.

## 5. Settings

- [x] 5.1 `DisplaySettings.SmoothScroll`, in lines per second, default 20.
      Chosen so the scroll is not the bottleneck at the default 4800 baud, where
      the wire itself delivers about six lines a second; roughly three times the
      original hardware, and three frames a line at 60 Hz.
- [x] 5.2 `[display] smooth-scroll` in the reader, the template and the state
      file, with the range and the meaning of zero documented in the template.
- [x] 5.3 `--smooth-scroll <lines/sec>` on the command line.
- [x] 5.4 `scroll <lines/sec>` at the local prompt, reported in the same form as
      the other live toggles, and listed in help.
- [x] 5.5 One ceiling, consulted by the parser, the reader and the flag, in the
      manner of `CrtEffect`.

## 6. Documentation

- [x] 6.1 README: the setting, the default, what zero means, and why it is
      faster than the hardware was.
- [x] 6.2 Check the help overlay still fits — `HelpFitsTests` exists because it
      did not once before.
- [x] 6.3 No em dashes in anything that renders on the CP437 grid.

## 7. Verification

- [x] 7.1 `dotnet test Baudacious.sln`.
- [x] 7.2 Add `shot --mid-scroll <0-1>` and inspect the slide at 0, 0.1, 0.5,
      0.9 and 1. A smooth scroll lives between frames, so a still cannot show it
      without being told where to stop, exactly as `--fade` exists for
      persistence. This is what caught the inverted direction: the image slid
      down and snapped, and a frame taken at 0.5 looks the same either way.
- [x] 7.3 Confirm the status bar does not move and nothing shows through it.
      Confirmed at every offset: the row arriving at the bottom is covered by
      the bar until it has risen past it.
- [x] 7.4 Confirm an idle screen is byte-identical to one rendered with the
      feature off. Three frames, including the status bar and the curvature and
      mask paths, hash the same as the binary built from HEAD.
- [x] 7.5 Run it and judge the speed. Signed off at the default 20 lines/sec.
