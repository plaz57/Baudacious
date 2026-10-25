## ADDED Requirements

### Requirement: Smooth scrolling

The system SHALL be able to animate a scroll, sliding the image up over several
frames instead of advancing it a whole cell in one, in the manner of DECSCLM on
a VT100 or VT220. The rate SHALL be expressed in lines per second, SHALL be
adjustable, and SHALL be settable to zero, which means the jump scroll.

The default rate SHALL be faster than the original hardware's, which managed
roughly six lines a second: that rate is authentic and unusable, and a setting
nobody leaves enabled has not reproduced anything.

A scroll in progress SHALL NOT change what the grid contains, what the governor
releases, or when a byte is considered delivered. It is a property of how the
grid is drawn.

The status bar SHALL NOT scroll. Like curvature, it is chrome rather than screen
content.

#### Scenario: A line arrives

- **WHEN** one line scrolls the screen with smooth scrolling enabled
- **THEN** the image slides up by a fraction of a cell on each successive frame
  until it has advanced exactly one cell, and the row leaving the top is visible
  part-way off the screen while it does

#### Scenario: Smooth scrolling off

- **WHEN** the rate is set to zero
- **THEN** a scroll advances the image by a whole cell in a single frame, and
  the rendered output is identical to the same screen with the feature absent

#### Scenario: Output outruns the animation

- **WHEN** lines scroll faster than the configured rate can retire them
- **THEN** the backlog is capped, the scroll advances to the current screen
  rather than falling further behind, and no output is delayed, dropped or
  reordered to let the animation keep up

#### Scenario: The status bar holds still

- **WHEN** the screen is mid-scroll
- **THEN** the status bar is drawn at its fixed position, fully legible, with
  no part of the scrolling image showing through or past it

#### Scenario: Nothing is lost off the bottom

- **WHEN** the screen is mid-scroll
- **THEN** the row arriving at the bottom is drawn in the space it is sliding
  through, so the gap the scroll opens is never left unlit

#### Scenario: A still screen costs nothing

- **WHEN** no scroll is in progress
- **THEN** the frame is identical to one rendered with smooth scrolling
  disabled, and the idle frame rate is unaffected
