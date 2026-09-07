## MODIFIED Requirements

### Requirement: CRT post-processing

The system SHALL apply a post-process pass over the rendered text giving the
display the character of a phosphor CRT. The pass SHALL include phosphor bloom
around bright glyphs, horizontal scanlines, edge vignetting, screen curvature,
aperture-grille masking, and phosphor persistence. Each effect's intensity
SHALL be individually adjustable, and the whole pass SHALL be disableable.

Every effect SHALL default to an intensity at which all 256 CP437 glyphs remain
individually distinguishable, and SHALL be able to be turned off individually
without affecting the others.

#### Scenario: Effects on by default

- **WHEN** the application starts with default settings
- **THEN** glyphs show a soft glow bleeding into neighbouring pixels, scanlines
  are visible, and the screen edges are darkened

#### Scenario: Disabling post-processing

- **WHEN** the user disables the CRT pass
- **THEN** glyphs render as crisp unfiltered pixels and frame time decreases

#### Scenario: Adjusting one effect

- **WHEN** the user sets bloom intensity to zero while leaving scanlines on
- **THEN** the glow disappears and the scanlines remain

#### Scenario: Text legibility is not sacrificed

- **WHEN** the CRT pass is active at default intensity
- **THEN** every glyph in the CP437 set remains individually distinguishable,
  including the ones that differ only in fine detail

### Requirement: Screen curvature

The system SHALL be able to render the text as though it were on the curved face
of a tube, bending the image outward from the centre. Areas the curved screen
does not cover SHALL render as unlit glass rather than as stretched or repeated
edge pixels.

The status bar SHALL NOT be curved: it is chrome rather than screen content, and
it is the one line that must stay legible at all times.

#### Scenario: Curvature at its default

- **WHEN** curvature is at its default intensity
- **THEN** straight rows of text bow outward slightly, most visibly near the
  edges, and every glyph remains distinguishable

#### Scenario: Curvature off

- **WHEN** curvature is set to zero
- **THEN** the image is geometrically identical to the pass without curvature,
  and no pixels are lost at the edges

#### Scenario: Outside the glass

- **WHEN** curvature moves part of the output outside the source image
- **THEN** those pixels render as unlit screen, not as a smear of the nearest
  edge pixel

### Requirement: Aperture-grille masking

The system SHALL be able to weight output pixels into vertical red, green and
blue stripes, in the manner of an aperture-grille tube, with the stripe phase
fixed to the output pixel grid so that the mask does not move when the terminal
grid is resized.

Because masking removes light by construction, the system SHALL compensate for
the brightness it costs across the usable range of intensities, so that turning
the mask on changes the character of the image rather than its overall level. At
extreme intensities the compensation is bounded by the top of the output range,
and the system is not required to recover light that clipping removes.

#### Scenario: The mask is visible up close

- **WHEN** masking is on and the image is examined at pixel level
- **THEN** adjacent output pixels carry different colour weights in a repeating
  three-pixel pattern

#### Scenario: Brightness is preserved

- **WHEN** masking is turned on at a moderate intensity
- **THEN** the mean luminance of a screen of text is substantially unchanged

#### Scenario: Masking is not a more faithful monochrome

- **WHEN** the phosphor palette is one of the monochrome ones
- **THEN** masking is not applied unless the user asks for it, an aperture grille
  being a colour-tube artifact rather than something a monochrome tube had

#### Scenario: The mask does not swim

- **WHEN** the window is resized or the font scale changes
- **THEN** the stripe pattern stays aligned to the output pixels rather than to
  the character cells

### Requirement: Phosphor persistence

The system SHALL be able to retain the previous frame's output and combine it
with the current frame so that light decays over several frames rather than
disappearing at once.

The combination SHALL take the brighter of the decayed history and the current
frame at each pixel, not a blend of them: a phosphor emits light, so a newly lit
pixel SHALL NOT be dimmed by a dark history, and a pixel that has gone dark
SHALL fade rather than cut.

#### Scenario: Light that goes away fades

- **WHEN** persistence is on and a lit area of the screen is cleared, scrolled or
  overwritten
- **THEN** what was there fades over several frames rather than vanishing in one

#### Scenario: Text arriving leaves no trail

- **WHEN** persistence is on and text is arriving at a low line rate
- **THEN** no trail follows the cursor, because writing a character lights pixels
  rather than darkening them, and the cell the cursor leaves has the new glyph
  drawn into it in the same frame

#### Scenario: Decay is bounded

- **WHEN** persistence is at its default and the screen is cleared
- **THEN** the retained image fades to unlit within a small number of frames
  rather than remaining indefinitely

#### Scenario: Newly lit pixels are at full brightness

- **WHEN** a glyph is drawn where the previous frame was dark
- **THEN** that glyph is as bright as it would be with persistence off

#### Scenario: Persistence off

- **WHEN** persistence is set to zero
- **THEN** each frame is composited exactly as it would be without the retained
  target, and no history is visible

#### Scenario: Resizing while a history is retained

- **WHEN** the window is resized while persistence is on
- **THEN** the retained target follows the new size without leaving a stretched
  or stale image behind
