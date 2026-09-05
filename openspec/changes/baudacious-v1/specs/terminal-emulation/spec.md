## Purpose

Interprets the metered byte stream as terminal control sequences and maintains
the resulting character grid and scrollback, so that shells and the programs
they run behave as they would in any competent modern terminal.

## ADDED Requirements

### Requirement: The emulator maintains a character grid

The system SHALL maintain a grid of cells, each holding a character, a
foreground colour, a background colour, and attribute flags for bold, faint,
italic, underline, blink, reverse, invisible, and strikethrough. The grid SHALL
have a cursor position and SHALL be resizable.

#### Scenario: Printable text advances the cursor

- **WHEN** the byte `A` is interpreted with the cursor at row 1 column 1
- **THEN** cell (1,1) holds `A` with the current attributes and the cursor moves
  to column 2

#### Scenario: Attributes apply to subsequently written cells

- **WHEN** an SGR sequence sets bold red foreground and then `Hi` is written
- **THEN** both cells carry the bold flag and the red foreground, and cells
  written before the sequence are unchanged

### Requirement: Supported escape sequences

The system SHALL interpret, at minimum: cursor positioning (CUP, CUU, CUD, CUF,
CUB, CHA, VPA, HVP), cursor save and restore (DECSC/DECRC and their CSI forms),
erase in display and line (ED, EL), insert and delete lines and characters
(IL, DL, ICH, DCH, ECH), scroll region definition (DECSTBM), scroll up and down
(SU, SD), index and reverse index (IND, RI, NEL), SGR attributes including the
16 base colours, the bright set, 256-colour indexed and 24-bit truecolor forms,
autowrap mode (DECAWM), cursor visibility (DECTCEM), the alternate screen
buffer (private modes 1047, 1048, 1049), bracketed paste mode (2004), tab set
and clear (HTS, TBC) with cursor forward tabulation, device status and identity
reports (DSR, DA), soft and hard reset (DECSTR, RIS), and OSC 0/2 window title.

#### Scenario: Absolute cursor addressing

- **WHEN** the sequence `ESC [ 10 ; 20 H` is interpreted
- **THEN** the cursor is at row 10, column 20

#### Scenario: Truecolor

- **WHEN** the sequence `ESC [ 38 ; 2 ; 255 ; 128 ; 0 m` is interpreted
- **THEN** the current foreground colour is exactly RGB(255,128,0)

#### Scenario: Alternate screen buffer

- **WHEN** a full-screen program enters the alternate buffer, draws, and exits it
- **THEN** the primary buffer's contents and cursor position are restored
  unchanged, and nothing the program drew was added to scrollback

#### Scenario: Scroll region

- **WHEN** a scroll region of rows 5 through 10 is set and text scrolls at row 10
- **THEN** only rows 5 through 10 shift and rows outside the region are untouched

### Requirement: Unrecognised sequences are consumed silently

The system SHALL parse and discard escape sequences it does not implement
without emitting their bytes as visible text and without corrupting the grid.
Malformed or truncated sequences SHALL NOT cause the parser to fail or hang.

#### Scenario: An unsupported CSI sequence

- **WHEN** a well-formed CSI sequence with an unimplemented final byte arrives
- **THEN** the sequence is consumed, nothing is displayed for it, and the
  following text renders normally

#### Scenario: A truncated sequence

- **WHEN** a byte stream ends mid-sequence and the shell then exits
- **THEN** the parser holds its state without error and no stray characters
  appear

#### Scenario: Sequence split across metering boundaries

- **WHEN** an escape sequence's bytes are released across several frames because
  of the baud rate
- **THEN** the sequence takes effect only when its final byte is released, and
  its partial bytes are never displayed as text

### Requirement: Scrollback

The system SHALL retain lines that scroll off the top of the primary grid in a
scrollback buffer whose line capacity is configurable, defaulting to 10,000
lines. The user SHALL be able to scroll back through it and return to the live
view.

#### Scenario: Scrolling back

- **WHEN** 200 lines have scrolled off and the user scrolls up
- **THEN** the earlier lines are shown with their original characters, colours,
  and attributes

#### Scenario: New output while scrolled back

- **WHEN** the user is scrolled back and new output is released
- **THEN** the view stays where the user put it, the new output is recorded, and
  an indicator shows the live view has advanced

#### Scenario: Capacity is bounded

- **WHEN** the scrollback exceeds its configured capacity
- **THEN** the oldest lines are discarded and memory use stays bounded

### Requirement: Character encoding

The system SHALL decode the shell's output as UTF-8. Bytes that do not form a
valid UTF-8 sequence SHALL be replaced with a substitution glyph rather than
discarded or treated as control codes. Code points the active font cannot
render SHALL display a visible substitution glyph.

#### Scenario: Valid UTF-8

- **WHEN** the shell emits box-drawing characters as UTF-8
- **THEN** they display as box-drawing characters

#### Scenario: Invalid byte sequence

- **WHEN** an invalid UTF-8 byte is released
- **THEN** a single substitution glyph appears and parsing continues from the
  next byte

#### Scenario: Unrenderable code point

- **WHEN** a code point outside the active font's coverage is released
- **THEN** a substitution glyph is shown in that cell and the cursor advances
  normally

### Requirement: Resize preserves content

On grid resize, the system SHALL preserve existing content, reflowing lines to
the new width where they were soft-wrapped and preserving hard line breaks. The
cursor SHALL remain on its logical line.

#### Scenario: Narrowing the grid

- **WHEN** the grid narrows from 120 to 80 columns with soft-wrapped lines
  present
- **THEN** those lines re-wrap at 80 columns and no characters are lost

#### Scenario: Widening the grid

- **WHEN** the grid widens and previously wrapped lines now fit
- **THEN** those lines are rejoined and hard line breaks remain breaks
