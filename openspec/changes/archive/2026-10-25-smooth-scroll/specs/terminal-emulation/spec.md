## MODIFIED Requirements

### Requirement: Supported escape sequences

The system SHALL interpret, at minimum: cursor positioning (CUP, CUU, CUD, CUF,
CUB, CHA, VPA, HVP), cursor save and restore (DECSC/DECRC and their CSI forms),
erase in display and line (ED, EL), insert and delete lines and characters
(IL, DL, ICH, DCH, ECH), scroll region definition (DECSTBM), scroll up and down
(SU, SD), index and reverse index (IND, RI, NEL), SGR attributes including the
16 base colors, the bright set, 256-color indexed and 24-bit truecolor forms,
autowrap mode (DECAWM), smooth scroll mode (DECSCLM), cursor visibility
(DECTCEM), the alternate screen buffer (private modes 1047, 1048, 1049),
bracketed paste mode (2004), tab set and clear (HTS, TBC) with cursor forward
tabulation, device status and identity reports (DSR, DA), soft and hard reset
(DECSTR, RIS), and OSC 0/2 window title.

#### Scenario: Absolute cursor addressing

- **WHEN** the sequence `ESC [ 10 ; 20 H` is interpreted
- **THEN** the cursor is at row 10, column 20

#### Scenario: Truecolor

- **WHEN** the sequence `ESC [ 38 ; 2 ; 255 ; 128 ; 0 m` is interpreted
- **THEN** the current foreground color is exactly RGB(255,128,0)

#### Scenario: Alternate screen buffer

- **WHEN** a full-screen program enters the alternate buffer, draws, and exits it
- **THEN** the primary buffer's contents and cursor position are restored
  unchanged, and nothing the program drew was added to scrollback

#### Scenario: Scroll region

- **WHEN** a scroll region of rows 5 through 10 is set and text scrolls at row 10
- **THEN** only rows 5 through 10 shift and rows outside the region are untouched

#### Scenario: Smooth scroll mode

- **WHEN** a program sends `ESC [ ? 4 l` and later `ESC [ ? 4 h`
- **THEN** the emulator first records that jump scrolling was demanded and then
  that the demand was withdrawn, and a soft or hard reset withdraws it too

#### Scenario: The mode is not the SGR attribute

- **WHEN** the sequence `ESC [ 4 m` is interpreted
- **THEN** subsequent cells are underlined and the smooth scroll mode is
  unchanged
