## Purpose

Stores the user's settings and shell profiles in a file they can read and edit
by hand, and restores the state of the last session on startup so Baudacious comes
back the way it was left.

## ADDED Requirements

### Requirement: A single human-editable config file

The system SHALL read its settings from one text configuration file in a
per-user application data location, SHALL create it with commented defaults on
first run, and SHALL name its location in the help overlay.

#### Scenario: First run

- **WHEN** the application starts with no configuration file present
- **THEN** a file is created containing every setting at its default value with
  explanatory comments, and the application starts with those defaults

#### Scenario: Locating the file

- **WHEN** the user opens the help overlay
- **THEN** the full path of the configuration file is shown

### Requirement: Configured settings

The configuration SHALL cover at minimum: baud rate, framing, shell profiles
and which is the default, phosphor palette, font scale, CRT effect toggles and
per-effect intensities, cursor shape and blink interval, scrollback line
capacity, window geometry, and the local command key bindings.

#### Scenario: A setting takes effect

- **WHEN** the configuration sets the default baud rate to 2400
- **THEN** a newly started session begins at 2400 baud

### Requirement: Invalid configuration is reported, not fatal

When the configuration file cannot be parsed or contains an invalid value, the
system SHALL start using defaults for the affected settings, SHALL display what
was wrong including the location in the file, and SHALL NOT overwrite the
user's file.

#### Scenario: Malformed file

- **WHEN** the configuration file contains a syntax error
- **THEN** the application starts with all defaults, reports the syntax error
  and where it is, and leaves the file untouched

#### Scenario: One invalid value

- **WHEN** the baud rate is set to a non-numeric value but the rest is valid
- **THEN** every other setting is honoured, the baud rate falls back to its
  default, and the specific problem is reported

### Requirement: Hot reload

The system SHALL detect changes to the configuration file while running and
apply them without restarting the application or disturbing any running shell.

#### Scenario: Editing while running

- **WHEN** the user edits the phosphor palette in the file and saves it
- **THEN** the display adopts the new palette within one second and the running
  shell is unaffected

#### Scenario: Saving a broken edit

- **WHEN** a save introduces a syntax error
- **THEN** the previously loaded settings remain in effect, the error is
  reported, and the application continues running

### Requirement: Session state persists across runs

The system SHALL persist the last-used baud rate, framing, phosphor palette,
font scale, CRT settings, and window geometry when it exits, and SHALL restore
them on the next start. Persisted state SHALL be kept separately from the
user's hand-edited configuration so that saving state never rewrites their file.

#### Scenario: Restoring after exit

- **WHEN** the user sets 9600 baud and amber phosphor and then exits
- **THEN** the next start opens at 9600 baud with amber phosphor

#### Scenario: State does not clobber the config file

- **WHEN** settings are changed at runtime and the application exits
- **THEN** the user's configuration file is byte-for-byte unchanged, including
  its comments and formatting

#### Scenario: Explicit config wins on a fresh install

- **WHEN** no persisted state exists but the configuration file sets values
- **THEN** the configuration file's values are used
