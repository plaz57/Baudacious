## Purpose

Draws the character grid in Baudacious's own window with the visual character of a
phosphor CRT, at a frame rate steady enough that metered text appears to arrive
smoothly rather than in visible clumps.

## ADDED Requirements

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
  with the background colour

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
green (P1), amber (P3), and white (P4) — in which the terminal's colour
attributes are mapped to intensity levels of the single phosphor colour. The
system SHALL also provide a full-colour mode in which colour attributes render
as their actual colours.

#### Scenario: Green phosphor

- **WHEN** the green phosphor palette is active and the shell emits red text
- **THEN** the text renders in green at an intensity derived from the red's
  luminance, and no red appears on screen

#### Scenario: Full-colour mode

- **WHEN** full-colour mode is active
- **THEN** colour attributes render as their specified colours

### Requirement: CRT post-processing

The system SHALL apply a post-process pass over the rendered text giving the
display the character of a phosphor CRT. The v1 pass SHALL include phosphor
bloom around bright glyphs, horizontal scanlines, and edge vignetting. Each
effect's intensity SHALL be individually adjustable, and the whole pass SHALL
be disableable.

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
