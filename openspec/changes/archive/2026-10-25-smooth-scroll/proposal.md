## Why

A VT100 or VT220 could be told to scroll smoothly — DECSCLM, `CSI ? 4 h` — and
instead of the screen jumping up a whole character cell when a line arrived, the
raster slid up a couple of scan lines per refresh until it got there.

It is the right kind of detail for this project, and it is the same kind of
detail as the baud rate: a property of the hardware that changed how reading
felt, which no modern terminal reproduces because no modern terminal has a
reason to.

**It was also too slow.** DEC advanced the image a couple of scan lines per
refresh, which put it somewhere around six lines a second. Fine for watching one
line arrive; infuriating for anything longer, which is why people turned it off.
So this ships it adjustable and faster than the original by default. Authenticity
that nobody leaves switched on has not been reproduced, it has been exhibited.

It should also compose well with what is already here. A smooth scroll moves
every lit pixel on the screen every frame, and phosphor persistence renders
exactly that — light going away. The two together are the strongest case the CRT
pass has.

## What Changes

A scroll animates over several frames instead of landing in one, at a rate the
user sets in lines per second, with 0 meaning the jump scroll of today.

**Where the work happens.** The grid, the overlay and the status row are already
three separate passes into one offscreen target, and the status row is drawn
last. So a smooth scroll is: emit one extra row of content above the grid, shift
every grid instance up by a sub-cell offset, and let the status row overdraw the
part that spills past the bottom. The row leaving the top clips against the
viewport on its own.

That means the render target does not change size, and `Present`, the blit path,
the CRT pass and `shot` are all untouched. The alternative — an overscan row
baked into the target — would have reached into every one of them.

**Keeping up.** A real terminal could smooth scroll honestly because it stopped
accepting data while it scrolled. Baudacious cannot: the governor owns the rate
at which bytes arrive, and a second thing throttling the same stream would make
the line rate a lie. So when lines arrive faster than the animation can retire
them the backlog is capped and the scroll snaps, which is the honest failure:
the line rate is the promise, and the scroll is decoration over it.

**`CSI ? 4 h` is honoured too**, so a program that asks for smooth scrolling gets
it. Almost nothing asks, which is why the setting exists; but a terminal that
advertises the mode and ignores it is worse than one that does neither.

### Not in this change

- Smooth scroll on the alternate screen buffer. Full-screen programs repaint
  rather than scroll, so there is nothing to animate and a great deal to get
  wrong.
- Smooth reverse scroll. The same machinery upside down, and nothing produces it
  often enough to justify the second code path yet.
- Throttling the shell while scrolling, as DEC did. That would put two things in
  charge of pacing.

## Impact

- **Affected specs:** `display-rendering` gains the scroll behaviour;
  `terminal-emulation` gains DECSCLM.
- **Affected code:** the text vertex shader and `TextRenderer.BuildInstances`, a
  new animator, `TerminalEmulator` for mode 4, and the settings, template, flag
  and prompt command that expose the rate.
- **Not affected:** the governor, the parser's byte accounting, the grid itself.
  The scroll is a property of how the grid is drawn, not of what it contains.
