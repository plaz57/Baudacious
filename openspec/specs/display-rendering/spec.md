# display-rendering Specification

## Purpose
Draws the character grid in Baudacious's own window with the visual character of a
phosphor CRT, at a frame rate steady enough that metered text appears to arrive
smoothly rather than in visible clumps.
## Requirements
### Requirement: Baudacious owns its window

The system SHALL render into a window it owns and control, not into a host
console. The window SHALL be resizable, SHALL support fullscreen, and SHALL
remember its size and position between runs.

#### Scenario: Launching

- **WHEN** the application starts
- **THEN** a window appears at its remembered size and position showing the
  character grid, with no host console window present

#### Scenario: Toggling fullscreen

- **WHEN** the user toggles fullscreen
- **THEN** the window fills the display, the grid dimensions are recalculated
  and propagated to the shell, and content is preserved

### Requirement: Frame pacing

The system SHALL present frames synchronised to the display refresh, SHALL
sustain the display's refresh rate while text is arriving, and SHALL reduce to
an idle rate when the screen is static and no bytes are pending. Frame time
SHALL remain under the refresh interval for a fully populated 200x60 grid.

#### Scenario: Smooth arrival at a low rate

- **WHEN** text arrives at 1200 baud on a 60 Hz display
- **THEN** two characters appear per frame at an even cadence, with no visible
  clumping or stutter

#### Scenario: High rate on a full grid

- **WHEN** text arrives at 115200 baud onto a 200x60 grid
- **THEN** the frame rate does not drop below the display refresh rate and no
  bytes are deferred to a later frame by the renderer

#### Scenario: Idle

- **WHEN** no bytes are pending and the screen has not changed for one second
- **THEN** the frame rate drops to at most 10 Hz and CPU and GPU use fall
  accordingly

### Requirement: Text is drawn from a fixed-cell glyph atlas

The system SHALL render text as fixed-size cells from a bitmap glyph atlas,
shipping with a CP437 IBM VGA face as the default. Cell size SHALL be
user-scalable in integer multiples, and the grid dimensions SHALL be derived
from the window size divided by the scaled cell size.

#### Scenario: Changing the scale

- **WHEN** the user increases the font scale from 2x to 3x
- **THEN** glyphs render three times their native size with no blurring from
  interpolation, the grid dimensions shrink accordingly, and the shell is
  notified of the new size

#### Scenario: Grid derived from window size

- **WHEN** the window's drawable area is 1280x800 with a 3x-scaled 8x16 cell
  (24x48 pixels)
- **THEN** the grid is 53 columns by 16 rows and the leftover margin is filled
  with the background color

### Requirement: The typeface is selectable

A single bitmap face cannot suit everyone: stroke weight at 8x16 is a matter of taste, and
a face that reads as crisp to one person reads as heavy to another. The system SHALL ship
more than one typeface, SHALL let the user choose between them, and SHALL apply a change
without restarting the session or disturbing the hosted shell.

Every bundled face SHALL be a bitmap font drawn for the cell size in use, not an outline
typeface rasterised down to it, and SHALL cover the CP437 repertoire. Faces SHALL differ
in weight, so that the choice is a real one.

#### Scenario: Choosing a lighter face

- **WHEN** the user selects a typeface with a lighter stroke weight
- **THEN** the display redraws in that face, the grid dimensions are unchanged because the
  cell size is unchanged, and the hosted shell is not signalled

#### Scenario: The choice persists

- **WHEN** the user selects a typeface and later restarts the application
- **THEN** the same typeface is in use

#### Scenario: A face that cannot be loaded

- **WHEN** a configured typeface is missing or unreadable
- **THEN** the system reports which one and falls back to a bundled default rather than
  starting with no glyphs

### Requirement: Phosphor palette

The system SHALL provide selectable monochrome phosphor palettes — at minimum
green (P1), amber (P3), and white (P4) — in which the terminal's color
attributes are mapped to intensity levels of the single phosphor color. The
system SHALL also provide a full-color mode in which color attributes render
as their actual colors.

#### Scenario: Green phosphor

- **WHEN** the green phosphor palette is active and the shell emits red text
- **THEN** the text renders in green at an intensity derived from the red's
  luminance, and no red appears on screen

#### Scenario: Full-color mode

- **WHEN** full-color mode is active
- **THEN** color attributes render as their specified colors

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

### Requirement: Post-processing is isolated from text rendering

The system SHALL render text to an offscreen target at native grid resolution
and apply all CRT effects as a subsequent pass over that target. Adding,
removing, or changing an effect SHALL require no change to how text is
rasterised.

#### Scenario: Adding a future effect

- **WHEN** a new effect such as barrel distortion is added to the pass
- **THEN** the text rasterisation stage is unchanged, and the effect composes
  with the existing effects

### Requirement: Cursor rendering

The system SHALL draw the cursor in a user-selectable shape (block, underline,
or vertical bar) with optional blinking at a configurable interval, and SHALL
hide it when the emulator's cursor-visibility state says it is hidden.

#### Scenario: Blinking block cursor

- **WHEN** a blinking block cursor is configured with a 500 ms interval
- **THEN** a filled block at the cursor cell alternates visible and hidden every
  500 ms

#### Scenario: Program hides the cursor

- **WHEN** the emulator's cursor-visibility state is off
- **THEN** no cursor is drawn regardless of the configured shape

### Requirement: Backlog indicator

The system SHALL display an unobtrusive indicator whenever unreleased bytes are
queued, showing that output is still arriving, and SHALL remove it when the
queue empties.

#### Scenario: Output pending

- **WHEN** bytes remain queued
- **THEN** an indicator is visible showing that output is still in flight

#### Scenario: Queue drains

- **WHEN** the last queued byte is released
- **THEN** the indicator disappears

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

