## Purpose

Translates keyboard and mouse input into the bytes a hosted shell expects, and
provides a client-side command layer for controlling Baudacious itself that never
competes with the shell for keystrokes.

## ADDED Requirements

### Requirement: Keystrokes reach the shell without metering delay

The system SHALL send input bytes to the hosted shell immediately on keypress,
with no baud metering applied to the outbound direction. Any echo of that input
is output from the shell and is therefore metered like all other output.

#### Scenario: Typing at a low rate

- **WHEN** the user types at 300 baud
- **THEN** each keystroke is delivered to the shell within one frame, while its
  echo appears on screen at the metered rate

#### Scenario: Typing ahead

- **WHEN** the user types faster than the echo can be painted
- **THEN** every keystroke reaches the shell in order and none are dropped

### Requirement: Key encoding

The system SHALL encode printable keys as their UTF-8 bytes; Ctrl combinations
with letters and the standard punctuation as their control codes; Enter,
Backspace, Tab, and Escape as their conventional bytes; and arrow keys,
function keys, Home, End, Page Up, Page Down, Insert, and Delete as the escape
sequences an xterm-compatible terminal sends, honouring the emulator's current
cursor-key mode.

#### Scenario: A control code

- **WHEN** the user presses Ctrl+D
- **THEN** the byte 0x04 is sent

#### Scenario: An arrow key in normal mode

- **WHEN** the user presses Up with cursor-key mode normal
- **THEN** the sequence `ESC [ A` is sent

#### Scenario: An arrow key in application mode

- **WHEN** the emulator is in application cursor-key mode and the user presses Up
- **THEN** the sequence `ESC O A` is sent

#### Scenario: Shell keybindings are not stolen

- **WHEN** the user presses any Ctrl+letter combination other than Ctrl+C
- **THEN** the corresponding control byte is sent to the shell and Baudacious takes
  no local action

### Requirement: Interrupt is the one shared chord

Ctrl+C SHALL send the interrupt to the hosted shell and SHALL additionally
discard the queued output backlog. When a selection is active, Ctrl+C SHALL
instead copy the selection and send nothing.

#### Scenario: Interrupt with no selection

- **WHEN** the user presses Ctrl+C with no selection active
- **THEN** the shell is interrupted and the backlog is discarded

#### Scenario: Copy with a selection

- **WHEN** text is selected and the user presses Ctrl+C
- **THEN** the selection is copied to the clipboard, the shell receives nothing,
  and the backlog is not discarded

### Requirement: Local commands are bound to Alt chords

All client-side commands SHALL be bound to Alt+key combinations. The system
SHALL NOT send anything to the shell for a bound Alt chord. Unbound Alt chords
SHALL be sent to the shell as ESC followed by the key's byte.

The v1 bindings SHALL be: Alt+B baud rate menu, Alt+F fast-forward the backlog,
Alt+Shift+F held 10x skim, Alt+P phosphor palette menu, Alt+G framing menu,
Alt+N new session, Alt+S switch session, Alt+C toggle the CRT pass, Alt+Z help
overlay, Alt+X quit.

#### Scenario: A bound chord

- **WHEN** the user presses Alt+B
- **THEN** the baud rate menu opens and no bytes are sent to the shell

#### Scenario: An unbound chord

- **WHEN** the user presses Alt+Q, which is not bound
- **THEN** ESC followed by `q` is sent to the shell

#### Scenario: Bindings are discoverable

- **WHEN** the user presses Alt+Z
- **THEN** an overlay lists every binding and its effect, and dismissing it
  returns to the session unchanged

### Requirement: Quitting is confirmed

Quitting kills a live shell and everything running in it, and the quit binding sits one key
away from bindings that are pressed casually. The system SHALL therefore NOT quit on a
single press: the first press SHALL arm the quit and say so, and only a second press within
a short window SHALL actually close. The armed state SHALL lapse on its own.

#### Scenario: One press does not quit

- **WHEN** the user presses the quit chord once
- **THEN** the application stays open, the hosted shell is untouched, and the status bar
  says a second press will quit

#### Scenario: Two presses quit

- **WHEN** the user presses the quit chord twice inside the confirmation window
- **THEN** the application closes and the hosted shell is terminated cleanly

#### Scenario: The arming lapses

- **WHEN** the user presses the quit chord once and then waits past the window
- **THEN** the armed state is dropped, the status bar returns to normal, and a later single
  press does not quit

### Requirement: A local command prompt

Ctrl+] SHALL open a local command prompt, distinct from the shell's own input
line, accepting typed commands with completion and a history. The prompt SHALL
support at minimum `baud <rate>`, `framing <spec>`, `flush`, `phosphor <name>`,
`fontsize <n>`, `crt on|off`, `session new <profile>`, `help`, and `quit`.
Escape SHALL dismiss it without effect.

#### Scenario: Setting the rate from the prompt

- **WHEN** the user presses Ctrl+] and enters `baud 2400`
- **THEN** the rate becomes 2400 baud, the prompt closes, and the shell received
  nothing

#### Scenario: An unrecognised command

- **WHEN** the user enters a command the prompt does not recognise
- **THEN** an error is shown locally, the prompt stays open, and nothing is sent
  to the shell

#### Scenario: Dismissing

- **WHEN** the user presses Escape at the local prompt
- **THEN** the prompt closes with no effect and no bytes are sent

### Requirement: Selection and clipboard

The system SHALL support selecting screen and scrollback text by mouse drag,
with double-click selecting a word, triple-click selecting a logical line, and
Shift+drag extending an existing selection. Copy SHALL place the selection on
the clipboard as plain text with trailing whitespace on each line removed.
Paste SHALL send the clipboard's text to the shell, using bracketed paste
markers when the emulator has that mode enabled.

#### Scenario: Copying wrapped text

- **WHEN** the user selects a soft-wrapped logical line and copies
- **THEN** the clipboard holds it as a single line without the wrap point
  becoming a newline

#### Scenario: Bracketed paste

- **WHEN** bracketed paste mode is enabled and the user pastes multi-line text
- **THEN** the text is sent framed by the bracketed paste markers, and the shell
  treats it as pasted rather than typed

#### Scenario: Paste is not metered

- **WHEN** the user pastes 4 KB at 300 baud
- **THEN** all 4 KB reaches the shell immediately, while the shell's echo of it
  is metered

### Requirement: Mouse reporting is passed through when requested

When the emulator has a mouse reporting mode enabled, the system SHALL encode
mouse events as that mode's sequences and send them to the shell instead of
treating them as selection.

#### Scenario: A program requests mouse reporting

- **WHEN** a program enables mouse reporting and the user clicks
- **THEN** a mouse event sequence is sent to the shell and no selection is made

#### Scenario: Selection override

- **WHEN** mouse reporting is enabled and the user drags with Shift held
- **THEN** a selection is made locally and no mouse events are sent

#### Scenario: Tracking level decides which events are worth sending

- **WHEN** a program has enabled click tracking rather than drag or motion
  tracking, and the pointer moves
- **THEN** no event is sent, because the program asked only for presses and
  releases

#### Scenario: Encoding is chosen separately from tracking level

- **WHEN** a program enables a tracking level and separately asks for the SGR
  encoding
- **THEN** events go out in the SGR form, and turning the tracking level off
  again leaves the requested encoding in place

#### Scenario: A position the legacy encoding cannot express

- **WHEN** the legacy encoding is in force and an event falls beyond the column
  or row it can represent
- **THEN** no event is sent, rather than one naming a different cell

#### Scenario: Scrolled back into history

- **WHEN** mouse reporting is enabled but the view is scrolled back
- **THEN** the mouse works locally, since the cells under the pointer are not
  the ones the program believes it is drawing on
