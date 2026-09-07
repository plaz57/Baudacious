## 1. Barrel distortion

- [x] 1.1 Add `Curvature` to `CrtSettings`, defaulting to **0.40**, threaded through the schema, template and `--curvature`. Chosen by looking at 0.18, 0.40 and 1.00: at 0.18 the bow has to be hunted for, at 0.40 it is obvious at a glance and costs nothing in legibility, at 1.00 it reads as a fisheye and the corners go dark
- [x] 1.2 Implement the UV transform at the top of the composite, with samples outside the source reading as unlit glass rather than clamped edge pixels. Scanlines take their phase from the distorted coordinate, so the raster curves with the glass instead of lying flat over it
- [x] 1.3 Exclude the status bar from the transform. **The bar is at the low end of v, not the high one** — the text vertex shader negates y so grid row 0 lands at the top of clip space, which puts the last row at v=0. Guarding the wrong end curved the bar and left the top row of text flat, which is how it was caught
- [x] 1.4 Verified: at zero the rendered PNG has the same MD5 as one from the pre-change shader (stash, render, unstash, render, compare), and at 0.40 over the glyph-check screen at both 80x25 and 100x30 every glyph is readable with nothing lost off the edges
- [x] 1.5 Frame cost at 200x60 magnify 2: **0.942 ms** with the pass on against a 0.97 ms baseline, so curvature is free within measurement noise — a handful of ALU ops per pixel, and the off-glass early-out gives back more than it takes at the corners

## 2. Aperture-grille masking

- [ ] 2.1 Add `Mask` to `CrtSettings` and thread it through as above
- [ ] 2.2 Implement the RGB stripe weighting, phase taken from the output pixel column so it stays fixed to the physical screen
- [ ] 2.3 Compensate the brightness the mask removes, and state the arithmetic in the shader
- [ ] 2.4 Verify: the stripes are visible at pixel level, mean luminance is substantially unchanged, and the pattern does not move when the grid resizes
- [ ] 2.5 Confirm the mask reads as a grille and not as moiré at 1x, 2x and 4x magnification — and record what it does at 1x, where there may not be pixels to spare

## 3. Phosphor persistence

- [ ] 3.1 Add `Persistence` to `CrtSettings` and thread it through as above
- [ ] 3.2 Add the retained output-resolution target and the ping-pong, using the destination parameter v1's task 6.1 put in place
- [ ] 3.3 Composite as a maximum against the decayed history rather than a blend, so a lit pixel is never dimmed by a dark past
- [ ] 3.4 Handle resize: the retained target follows the new size without leaving a stale or stretched image
- [ ] 3.5 Verify the decay is bounded — a cleared screen reaches unlit in a small number of frames rather than asymptotically never
- [ ] 3.6 Verify a trail actually follows arriving text at 300 baud, which is the whole reason for the effect
- [ ] 3.7 Measure the frame cost, and check the idle path: a decaying screen is still changing, so the 10 Hz idle drop must not freeze a fade half-done

## 4. Together

- [ ] 4.1 Choose the combined defaults by eye against all three at once, and record why each number is what it is
- [ ] 4.2 Confirm every CP437 glyph is still individually distinguishable at the combined defaults
- [ ] 4.3 Update the README's CRT table and the settings reference
- [ ] 4.4 Manual acceptance: a session at 1200 baud that looks like a tube and reads like a terminal
