## Why

Three CRT effects were named in the v1 proposal and cut from it: barrel
distortion, aperture-grille masking, and phosphor persistence. The cut was
deliberate — v1 needed a legible screen before it needed a convincing one — but
the pipeline was built expecting them, and one of them is worth more to
Baudacious than to any other terminal.

**Persistence is the effect this product was asking for.** In an instant
terminal, decay is only visible when the screen scrolls. At 300 baud characters
arrive one at a time, so a trail follows the cursor *as text is written*. The
feature that makes Baudacious what it is — metering onto the screen a character
at a time — is exactly the feature that makes a decaying phosphor visible.

Barrel distortion and the aperture grille are the two things that most separate
"a filter over a screenshot" from "a photograph of a tube": glass curvature, and
the fact that the light came from discrete phosphor stripes rather than a
continuous surface.

## What Changes

Three effects on the existing post-process pass, each with its own intensity in
`[crt]`, each defaulting low enough to stay legible, and all bypassed with the
existing `Alt+C`.

**Barrel distortion** — a UV transform at the start of the composite. Samples
outside the curved screen read as unlit glass rather than clamped edge pixels,
which is what makes the curvature read as depth instead of as a stretched
border.

**Aperture-grille masking** — a per-output-pixel RGB stripe weighting, phase
taken from the output pixel column so the triad stays fixed to the physical
screen and does not swim when the grid resizes. Costs brightness by
construction, so it compensates rather than simply darkening.

**Phosphor persistence** — a retained copy of the previous composited frame,
recombined as a maximum against the current one rather than a blend, because
phosphor *emits*: a bright pixel must not be dimmed by a dark history. This is
the only one of the three that changes the pass structure, which is why v1's
task 6.1 parameterised the destination target in advance.

### Not in this change

- Chromatic aberration and interlace flicker, still deferred. Both are cheap
  once these three exist; neither earns its place yet.
- Curvature applied to the *status bar*. The bar is chrome, not screen content,
  and bending it would make the one always-legible line harder to read.

## Impact

- **Affected specs:** `display-rendering` — the CRT post-processing requirement
  gains the three effects and the legibility constraint that governs their
  defaults.
- **Affected code:** `Baudacious.Render/CrtPass.cs` and its shader,
  `Baudacious.Render/CrtSettings.cs`, plus the settings schema, template and
  local prompt that expose the new intensities.
- **Not affected:** text rasterisation, the governor, the parser, the grid. That
  is the point of the isolation requirement, and this change is its test.
