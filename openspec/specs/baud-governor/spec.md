# baud-governor Specification

## Purpose
Meters the byte stream arriving from a hosted shell onto the display at a
user-selected baud rate and character framing, so that output appears at the
speed it would have travelled over a real serial line. This is the capability
the entire product exists to provide.
## Requirements
### Requirement: Selectable line rate

The system SHALL offer the line rates 110, 300, 1200, 2400, 4800, 9600, 19200,
38400, 57600, and 115200 baud, plus an `unlimited` mode that applies no
metering. The system SHALL also accept any arbitrary integer rate from 1 to
10,000,000 baud entered by the user.

#### Scenario: Selecting a standard rate

- **WHEN** the user selects 1200 baud with 8N1 framing
- **THEN** the system releases bytes to the display at 120 bytes per second

#### Scenario: Selecting unlimited

- **WHEN** the user selects `unlimited`
- **THEN** every byte received from the shell is released to the display in the
  same frame it arrives, with no queuing delay

#### Scenario: Rejecting an out-of-range rate

- **WHEN** the user enters a rate of 0 or a negative rate
- **THEN** the system rejects the input, reports the valid range, and leaves the
  current rate unchanged

### Requirement: Framing determines character rate

The system SHALL support the character framings 8N1, 7E1, 8N2, and 8E1, and
SHALL derive the byte release rate as `baud / bits_per_character`, where
`bits_per_character` counts the start bit, data bits, parity bit if any, and
stop bits.

#### Scenario: 8N1 framing

- **WHEN** framing is 8N1 (10 bits per character) at 2400 baud
- **THEN** bytes are released at 240 bytes per second

#### Scenario: 8N2 framing is slower than 8N1 at the same baud

- **WHEN** framing is 8N2 (11 bits per character) at 1200 baud
- **THEN** bytes are released at 109.09 bytes per second, measurably slower
  than the 120 bytes per second of 8N1 at the same baud rate

### Requirement: Metering applies to bytes, not to visible characters

The system SHALL meter the raw byte stream received from the shell before any
escape-sequence interpretation. Control characters, escape-sequence bytes, and
the continuation bytes of multi-byte UTF-8 encodings SHALL each consume wire
time identically to a printable character.

#### Scenario: An escape sequence costs wire time

- **WHEN** the shell emits the 5-byte sequence `ESC [ 3 1 m` at 1200 baud 8N1
- **THEN** approximately 41.7 ms of wire time elapses before the following byte
  is released, and no text appears during that interval

#### Scenario: A colored prompt takes longer than a plain one

- **WHEN** two prompts render the same 20 visible characters, one wrapped in SGR
  color sequences totalling 60 additional bytes
- **THEN** the colored prompt takes proportionally longer to finish painting,
  in the ratio 80:20

#### Scenario: Multi-byte UTF-8 costs per byte

- **WHEN** the shell emits a 3-byte UTF-8 encoded character
- **THEN** three characters' worth of wire time elapses before the next byte is
  released

### Requirement: The shell is not throttled

The system SHALL read from the hosted shell as fast as the shell produces
output and SHALL buffer unreleased bytes in a queue that grows as needed. The
shell process SHALL NOT be made to block on writing, and its execution time
SHALL NOT be affected by the selected baud rate.

#### Scenario: A fast command with slow output

- **WHEN** a command completes in 20 ms while producing 4 KB of output at 300
  baud
- **THEN** the command's exit is observed by the system immediately, the shell
  becomes ready for the next command, and the display continues painting the
  queued output for approximately 137 seconds

#### Scenario: A large backlog does not stall the reader

- **WHEN** 10 MB of output is produced at 1200 baud
- **THEN** the system buffers it without dropping bytes, without blocking the
  shell, and without unbounded memory growth beyond the size of the unreleased
  backlog

### Requirement: Timing accuracy

The system SHALL derive release timing from a monotonic clock measuring elapsed
wall-clock time. Over any measurement window of 10 seconds or longer during
which the queue is continuously non-empty, the number of bytes released SHALL
be within 0.5% of the nominal rate for the configured baud and framing.

#### Scenario: Accuracy under a sustained stream

- **WHEN** a continuous stream is metered at 9600 baud 8N1 (960 bytes/sec) for
  60 seconds
- **THEN** between 57,312 and 57,888 bytes have been released

#### Scenario: A dropped frame does not slow the line

- **WHEN** the renderer stalls for 250 ms and then resumes
- **THEN** the bytes owed for that 250 ms are released on resumption, and the
  cumulative byte count remains within tolerance of the nominal rate

#### Scenario: Fractional per-frame budgets do not accumulate error

- **WHEN** the rate is 300 baud 8N1 (30 bytes/sec) and the display refreshes at
  60 Hz, giving a budget of 0.5 bytes per frame
- **THEN** bytes are released on alternating frames and exactly 30 bytes are
  released per second, with no drift

### Requirement: Rate changes take effect immediately without disturbing the session

The system SHALL allow the baud rate and framing to be changed at any time. A
change SHALL apply to the bytes still queued and SHALL NOT restart, signal, or
otherwise disturb the hosted shell, and SHALL NOT discard queued bytes.

#### Scenario: Speeding up mid-stream

- **WHEN** the user changes from 300 to 9600 baud while 2 KB remains queued
- **THEN** the remaining bytes drain at 960 bytes/sec starting from the next
  frame, and the hosted shell is unaffected

### Requirement: Interrupt discards the backlog

When the user issues the interrupt gesture, the system SHALL both deliver the
interrupt to the hosted shell and discard all queued unreleased bytes.

#### Scenario: Interrupting a long listing

- **WHEN** 500 KB is queued at 1200 baud and the user presses Ctrl+C
- **THEN** the interrupt is delivered to the shell, the queue is emptied, no
  further queued output is painted, and the next prompt appears once it arrives
  and drains

### Requirement: Fast-forward releases the backlog without signalling the shell

The system SHALL provide a fast-forward control that releases the entire queued
backlog to the display immediately, and a separate held control that multiplies
the release rate by ten for as long as it is held. Neither SHALL send any signal
or byte to the hosted shell.

#### Scenario: Flushing the queue

- **WHEN** 500 KB is queued and the user presses the fast-forward control
- **THEN** the entire backlog is interpreted and displayed in the current frame,
  the final display state matches what unthrottled output would have produced,
  and the shell receives nothing

#### Scenario: Held 10x skim

- **WHEN** the rate is 1200 baud and the user holds the 10x control for 2
  seconds
- **THEN** approximately 2400 bytes are released during that interval
- **AND WHEN** the control is released
- **THEN** the rate returns to 120 bytes/sec

### Requirement: Queue state is observable

The system SHALL expose the current size of the unreleased backlog and the
estimated time to drain it at the current rate, so that the interface can
indicate to the user that output is still pending.

#### Scenario: Reporting drain time

- **WHEN** 12,000 bytes are queued at 1200 baud 8N1
- **THEN** the system reports a pending backlog of 12,000 bytes and an estimated
  drain time of 100 seconds

