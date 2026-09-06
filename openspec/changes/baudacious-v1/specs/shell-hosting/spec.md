## Purpose

Spawns, supervises, and communicates with the shell processes that Baudacious
hosts, presenting each as a bidirectional byte stream with a resizable
character grid, independent of which shell or hosting mechanism is used.

## ADDED Requirements

### Requirement: A window can hold several sessions

The system SHALL allow more than one shell session in a window, showing one at a
time, and SHALL meter every session whether or not it is the one on screen. Each
session SHALL keep its own line rate, scrollback and selection; the palette,
magnification and CRT pass SHALL be shared by the window.

#### Scenario: Starting another shell

- **WHEN** the user asks for a new session
- **THEN** a second shell starts, becomes the one on screen, and the first keeps
  running

#### Scenario: Switching between them

- **WHEN** the user switches sessions
- **THEN** the other session's screen, scrollback and line rate are shown, and
  typing goes to that shell

#### Scenario: A background session keeps receiving

- **WHEN** a session that is not on screen produces output
- **THEN** that output is metered at that session's rate as it arrives, so
  switching to it shows a screen that kept pace rather than a backlog

#### Scenario: One session's shell exits

- **WHEN** a session's shell exits and everything it sent has been shown
- **THEN** that session closes, another is shown, and the window stays open

#### Scenario: The last session's shell exits

- **WHEN** the only session's shell exits
- **THEN** the window stays open showing its final output and the exit code

#### Scenario: The window closes

- **WHEN** the window is closed
- **THEN** every session's shell is terminated

### Requirement: Shell sessions are byte streams

The system SHALL present each hosted shell as a session exposing an outbound
byte stream to the shell, an inbound byte stream from the shell, a settable
grid size in rows and columns, and a terminated event carrying the exit code.
No behaviour above this layer SHALL depend on which shell or hosting mechanism
is in use.

#### Scenario: Writing to a session

- **WHEN** bytes are written to a session's outbound stream
- **THEN** the hosted shell receives them on its standard input as if typed

#### Scenario: Reading from a session

- **WHEN** the hosted shell writes to its standard output or standard error
- **THEN** those bytes appear on the session's inbound stream, interleaved in
  the order the shell produced them

### Requirement: v1 shell backends

The system SHALL ship with working profiles for PowerShell 7 (`pwsh`), the Windows
command processor (`cmd.exe`), and a Linux shell under the Windows Subsystem for
Linux (`wsl`). It SHALL make the hosting mechanism replaceable so that additional
backends can be added without changes above this capability.

The WSL profile SHALL accept an optional distribution name, and SHALL use WSL's own
default when none is given.

#### Scenario: Launching pwsh

- **WHEN** the user starts a session with the pwsh profile
- **THEN** a `pwsh` process is running, its banner and prompt arrive on the
  inbound stream, and typed commands execute

#### Scenario: Launching cmd.exe

- **WHEN** the user starts a session with the cmd profile
- **THEN** a `cmd.exe` process is running, its version banner and prompt arrive,
  and typed commands execute

#### Scenario: Launching a Linux shell

- **WHEN** the user starts a session with the wsl profile
- **THEN** a Linux shell is running under the Windows Subsystem for Linux, its
  prompt arrives, and typed commands execute
- **AND** UTF-8 output from that shell — box drawing, accented characters — is
  displayed correctly rather than as substitution glyphs

#### Scenario: Choosing a distribution

- **WHEN** the user names a distribution alongside the wsl profile
- **THEN** that distribution is launched rather than WSL's default

#### Scenario: pwsh is absent

- **WHEN** the pwsh profile is selected on a machine where `pwsh` is not on the
  PATH and not at its default install location
- **THEN** the system reports that the executable could not be found, names the
  path it looked for, and does not terminate the application

#### Scenario: WSL is absent

- **WHEN** the wsl profile is selected on a machine without the Windows Subsystem
  for Linux installed
- **THEN** the system says so and says how to install it, and does not terminate
  the application

### Requirement: Shell profiles are user-definable

A shell profile SHALL consist of a display name, an executable path, command
line arguments, an optional working directory, optional additional environment
variables, and an optional list of startup commands to send after launch. The
system SHALL launch any profile the user defines.

#### Scenario: A custom profile

- **WHEN** the user defines a profile pointing at a Python REPL with arguments
  and a working directory
- **THEN** selecting that profile launches that executable with those arguments
  in that directory

#### Scenario: Startup commands

- **WHEN** a profile specifies startup commands
- **THEN** those commands are sent to the shell after launch, and their output
  is subject to the same metering as all other output

### Requirement: PowerShell readline defaults are tamed by default

Because PowerShell's line editor repaints the entire input line on every
keystroke, the system SHALL by default send startup configuration to `pwsh`
sessions that disables predictive suggestions, disables syntax coloring, and
simplifies the prompt. This behaviour SHALL be disableable by the user per
profile. Taming SHALL reduce per-keystroke echo traffic by at least half against
the same shell's untamed defaults.

#### Scenario: Taming measurably reduces per-keystroke cost

- **WHEN** the same text is typed into a tamed and an untamed pwsh session
- **THEN** the tamed session's mean echo traffic per keystroke is at most half
  the untamed session's
- **AND** the tamed session's prompt redraw is at most half the untamed one's

#### Scenario: Opting out

- **WHEN** the user disables the readline taming for a profile
- **THEN** no startup configuration is injected and the shell's own defaults
  apply, including their byte cost

### Requirement: Shells can be run with a minimal line discipline

Per-keystroke echo cost is dominated by the shell's line editor, not by the shell
itself, and it varies by two orders of magnitude. The system SHALL therefore offer
each profile a **fast echo** mode that replaces the shell's rich line editor with
the console's own cooked line input, and SHALL be able to enter and leave that mode
on a running session without restarting it.

For `pwsh`, fast echo is achieved by removing the PSReadLine module; measured at
1.0 bytes per keystroke, identical to `cmd.exe`, against 96.9 for stock pwsh. For
`cmd.exe` the mode is a no-op, as its line discipline already costs 1 byte per key.

Fast echo trades away the rich editor's features — syntax coloring, inline
prediction, incremental history search, multi-line editing, and probably tab
completion — so the system SHALL NOT enable it silently at rates where the rich
editor is comfortable, and SHALL make the trade explicit wherever it is offered.

#### Scenario: Fast echo reaches parity with cmd

- **WHEN** a pwsh session is started in fast echo mode
- **THEN** typing one character causes at most 2 bytes of echo traffic

#### Scenario: Fast echo is the default at low rates

- **WHEN** a pwsh session is started while the selected rate is below the fast echo
  threshold (default 4800 baud)
- **THEN** the session starts in fast echo mode
- **AND** a one-line notice states that the rich line editor was disabled for this
  rate and how to re-enable it

#### Scenario: The rich editor is kept at high rates

- **WHEN** a pwsh session is started at 9600 baud or above
- **THEN** the session keeps its rich line editor, with prediction and syntax
  coloring tamed but present, and no notice is shown

#### Scenario: Toggling on a live session

- **WHEN** the user toggles fast echo on a running pwsh session
- **THEN** the line discipline changes for subsequent input without restarting the
  shell, and without losing the session's command history or working directory

#### Scenario: Toggling is a no-op where it cannot help

- **WHEN** the user toggles fast echo on a cmd.exe session
- **THEN** the system reports that this shell already uses a minimal line discipline
  and changes nothing

### Requirement: The usable rate floor is stated per shell

Even in its tamed configuration a rich line editor costs far more per keystroke
than a cooked one, so a given profile is comfortable only above a certain rate. The
system SHALL record a usable-rate floor per profile and SHALL warn the user, once
per session, when they select a rate below the floor of the active profile in its
current line-discipline mode.

Measured floors (see `phase-0-findings.md`): fast echo — 1.0 bytes per keystroke,
comfortable at every supported rate including 300 baud; tamed rich editor — 28.5
bytes per keystroke, comfortable from 9600 baud; untamed — 96.9 bytes per
keystroke.

The warning SHALL explain that the cost accumulates rather than being a fixed
delay: at 4 characters per second, 28.5 bytes per keystroke at 1200 baud generates
roughly 950 ms of echo for every second spent typing, so the backlog grows for as
long as the user keeps typing.

#### Scenario: Selecting a rate below the floor

- **WHEN** the user selects 1200 baud with a tamed rich-editor pwsh profile
- **THEN** a notice states how much wire each second of typing generates at this
  rate, that the backlog therefore grows rather than settling at a fixed delay,
  and offers fast echo as the remedy; the rate is applied anyway

#### Scenario: No warning where there is no problem

- **WHEN** the user selects 300 baud with a cmd.exe or wsl profile, or with a pwsh
  profile in fast echo mode
- **THEN** no warning is shown

#### Scenario: The warning does not nag

- **WHEN** the user has already been warned in this session and selects another rate
  below the floor
- **THEN** no further warning is shown

### Requirement: Grid size is propagated to the shell

The system SHALL inform the hosted shell of the current grid dimensions on
launch and whenever they change, so that the shell and the programs it runs
wrap and lay out their output correctly.

#### Scenario: Resizing the window

- **WHEN** the display grid changes from 80x24 to 120x40
- **THEN** the shell is notified of the new dimensions
- **AND** a subsequent command's output wraps at 120 columns

#### Scenario: Resize while output is queued

- **WHEN** the grid is resized while unreleased bytes remain queued
- **THEN** the queued bytes are neither discarded nor reordered, and the shell
  is notified of the new size

### Requirement: Shell exit is surfaced, not hidden

When a hosted shell terminates, the system SHALL allow all already-queued
output to finish draining at the current rate, then display the exit status,
and SHALL offer to restart the session. The application SHALL NOT exit merely
because a hosted shell did.

#### Scenario: Shell exits with output pending

- **WHEN** the user types `exit` at 300 baud with 900 bytes still queued
- **THEN** the remaining bytes drain over approximately 30 seconds, and only
  then is the exit status shown

#### Scenario: Shell crashes

- **WHEN** the hosted process terminates with a non-zero exit code
- **THEN** the exit code is displayed and the user is offered a restart

### Requirement: Sessions are cleaned up

The system SHALL terminate hosted shell processes and release their operating
system handles when a session is closed or the application exits, leaving no
orphaned processes.

#### Scenario: Closing the application with a live shell

- **WHEN** the application is closed while a shell is running
- **THEN** the hosted process and its child processes are terminated and its
  handles released
