# Phase 0 — ConPTY measurements

Measured 2026-09-04 on Windows 11 Pro 26200.9168, .NET 10.0.301, PowerShell 7.6.5
(MSIX build), grid 100x30. Raw numbers in `spike/captures/measurements.tsv`;
byte streams and arrival timings in `spike/captures/*.bin` / `*.timing`.

Phase 0 exists to answer one question before anything is built on it: **does
design.md D2 — meter raw bytes, ahead of the VT parser — survive contact with
ConPTY?** It does, more comfortably than expected. Two design corrections and one
spec correction fall out.

---

## 1. Getting ConPTY to work at all

Three defects in the interop, each of which failed quietly rather than loudly.
They are recorded because all three are easy to reintroduce.

**`CREATE_NO_WINDOW` silently defeats the pseudoconsole.** Passing any
console-creation flag to `CreateProcess` — `CREATE_NO_WINDOW`, `CREATE_NEW_CONSOLE`,
`DETACHED_PROCESS` — gives the child a console of its own, so it never attaches to
the pseudoconsole. Symptom: the output pipe stays empty forever and closing stdin
never reaches the shell. `dwCreationFlags` must be `EXTENDED_STARTUPINFO_PRESENT`
and nothing else.

**`PSEUDOCONSOLE_INHERIT_CURSOR` is actively harmful here.** With that flag,
ConPTY emits a Device Status Report (`ESC[6n`) during startup and withholds *all*
output until the terminal answers with `ESC[row;colR`. Worse, the hosted shell then
reaches end-of-input and exits with status 0 the moment it first reads — printing a
banner and prompt and then quietly dying about 100 ms in. The flag exists only to
carry a parent console's cursor position across, which is meaningless for an
application that owns its own window and starts on a fresh grid. **`dwFlags` must
be 0.** design.md D4 said to set this flag; that was wrong.

**The measurement harness has to be a real process.** When the hosting process is
launched by an agent tool harness that pipes its stdio, the hosted shell exits
early regardless of everything above, and every measurement reads as zero. This is
a property of the harness, not of Baudacious: a console-less windowed build
(`OutputType=WinExe`, `GetConsoleWindow()` returning 0 — exactly Baudacious's shape)
launched normally hosts ConPTY perfectly, with working input, output, resize and
shutdown. `AllocConsole` is *not* required and does not help. All measurements
below were taken via `Start-Process`.

**Shutdown ordering, confirmed working.** Close stdin, wait briefly for the shell
to exit on its own, `ClosePseudoConsole` *while the reader thread is still
draining*, then join the reader, then close the remaining handles. Measured
shutdown: 6–18 ms, no hung reader, no orphaned process. `ClosePseudoConsole` blocks
until ConPTY has flushed, so stopping the reader before this point deadlocks.

---

## 2. ConPTY is silent at rest

| Profile | Bytes over 5 s idle at a prompt |
|---|---|
| pwsh (defaults) | **0** |
| pwsh (tamed) | **0** |
| cmd.exe | **0** |

No unsolicited repaints, no keepalives, nothing. The design's leading risk —
"ConPTY repaint chattiness inflates byte volume unpredictably" — **is not real for
the idle case** and can be downgraded. A Baudacious session sitting at a prompt costs
exactly zero wire time, which is the authentic behaviour anyway.

---

## 3. Escape sequences are a third to a half of interactive traffic

This is the measurement that decides D2.

| Capture | Bytes | Text | Escape | Escape share |
|---|---|---|---|---|
| pwsh-tamed keystrokes | 2,561 | 1,322 | 1,226 | **47.9%** |
| pwsh-default keystrokes | 2,273 | 1,191 | 1,077 | **47.4%** |
| cmd git status (color) | 1,066 | 656 | 398 | **37.3%** |
| pwsh-tamed prompt redraws | 1,746 | 1,105 | 611 | **35.0%** |
| pwsh-default git status | 916 | 632 | 271 | 29.6% |
| pwsh-tamed listing | 2,389 | 1,700 | 657 | 27.5% |
| cmd listing | 933 | 785 | 120 | 12.9% |
| cmd `dir /s` | 68,699 | 65,816 | 411 | 0.6% |

**D2 is confirmed, and the margin is large.** For everything interactive —
typing, prompts, colored output — between a third and a half of the wire time is
spent on bytes that draw no glyph. Metering decoded glyphs instead of raw bytes
would make a pwsh session arrive roughly twice as fast as a real line would deliver
it, and would erase precisely the uneven cadence that the product exists to
reproduce. Only bulk listings dilute the ratio, and those are the case where
timing fidelity matters least.

---

## 4. Cost per keystroke — the number that constrains the product

Bytes of echo traffic caused by pressing one key, averaged over 21 keys:

| Profile | Bytes/key | Min | Max |
|---|---|---|---|
| cmd.exe | **1.0** | 1 | 1 |
| pwsh, tamed | **38.2** | 18 | 57 |
| pwsh, defaults | **96.9** | 49 | 209 |

Converted to the delay before your own keystroke appears (8N1, so baud ÷ 10
bytes/sec):

| Baud | cmd | pwsh tamed | pwsh default |
|---|---|---|---|
| 300 | 33 ms | 1.27 s | 3.23 s |
| 1200 | 8 ms | 318 ms | 808 ms |
| 2400 | 4 ms | 159 ms | 404 ms |
| 9600 | 1 ms | 40 ms | 101 ms |
| 19200 | <1 ms | 20 ms | 51 ms |

Three conclusions:

1. **cmd.exe echoes one byte per keystroke and is comfortable at every rate,
   including 300 baud.** It is genuinely a 1980s-grade line discipline. This makes
   cmd, not pwsh, the shell that shows off what Baudacious is for.
2. **The taming in specs/shell-hosting works** — it cuts pwsh's per-keystroke cost
   by 61% (96.9 → 38.2) and its prompt redraw by 71% (21 → 6 bytes). It is worth
   keeping and worth being the default.
3. **It is not enough.** PSReadLine repaints the whole input line on every
   keystroke, and 38 bytes is close to the floor for that design. Tamed pwsh needs
   9600 baud before typing feels immediate. Below ~2400 baud, pwsh is a shell you
   watch, not one you type into.

### 4a. How close can pwsh get to cmd? All the way — by dropping PSReadLine

Follow-up probe, same method, same typed string, measured per configuration:

| Configuration | Bytes/key | min | max | vs cmd |
|---|---|---|---|---|
| pwsh, stock | 103.2 | 49 | 343 | 103x |
| pwsh `-NoProfile` | 96.9 | 49 | 209 | 97x |
| + `PredictionSource None` | 39.1 | 18 | 58 | 39x |
| + syntax colors neutralised | 29.4 | 13 | 40 | 29x |
| + plain prompt | 28.5 | 13 | 39 | 28x |
| **pwsh, PSReadLine removed** | **1.0** | **1** | **1** | **1x** |
| cmd.exe (reference) | 1.0 | 1 | 1 | 1x |

Typing lag generated per keystroke (8N1):

| Configuration | 300 | 1200 | 2400 | 9600 |
|---|---|---|---|---|
| pwsh, stock | 3.44 s | 860 ms | 430 ms | 108 ms |
| pwsh, fully tamed | 949 ms | 237 ms | 119 ms | 30 ms |
| **pwsh, PSReadLine removed** | **33 ms** | **8 ms** | **4 ms** | **1 ms** |

`Remove-Module PSReadLine` makes PowerShell fall back to the console host's cooked
line input — the same line discipline cmd.exe uses — and the result is **exact
parity with cmd at 1.0 bytes per keystroke, min 1, max 1**. Not "close to": the
same number.

Two things this probe also settled:

- **The original taming was partly broken.** It set PSReadLine colors from a
  single-quoted PowerShell string, which does not interpolate, so the literal text
  `$([char]27)[0m` was passed as a color and silently ignored. Only the prediction
  setting was actually taking effect. Fixed; the corrected version is what produces
  the 28.5 figure above, against 38.2 previously reported.
- **Incremental taming cannot close the gap.** Turning prediction off is the single
  biggest win (−60%) and the rest together add another −27%, but the floor is ~28
  bytes because PSReadLine repaints the whole input line every keystroke. That is
  its design, not a setting.

**The cost of removing PSReadLine** is its feature set: syntax highlighting, inline
prediction, `Ctrl+R` history search, rich multi-line editing, and its persistent
history file. Tab completion is very likely lost too — PSReadLine provides it in
modern PowerShell — but the micro-test written to confirm this returned 0 bytes for
Tab in *both* configurations, including the one where completion certainly works,
so the test was faulty and the question is recorded as **unverified**. It should be
answered by hand in Phase 3, when there is a real terminal to type into.

### 4b. Why the per-keystroke number matters more than it looks

The figure is not a fixed latency on one key; it is wire time *generated* per key,
and it accumulates. At a normal 4 characters/second, tamed pwsh at 1200 baud
generates 4 × 237 ms ≈ 950 ms of echo per second of typing, so the backlog grows
for as long as the user keeps typing and only drains when they stop. cmd.exe and
PSReadLine-free pwsh generate 33 ms per second of typing at the same rate and never
accumulate at all.

This is the difference between a terminal that feels slow and one that feels
broken, and it is why the fix below is a v1 concern rather than a polish item.

---

## 5. Bulk output, and why fast-forward is not optional

| Command | Bytes | 300 baud | 1200 baud | 9600 baud | 115200 baud |
|---|---|---|---|---|---|
| `dir` | 738 | 25 s | 6.2 s | 0.8 s | 0.06 s |
| `git status` (color) | 871 | 29 s | 7.3 s | 0.9 s | 0.08 s |
| `dir /s` | 68,504 | 38 min | 9.5 min | 71 s | 6 s |

A recursive listing at 1200 baud takes nine and a half minutes. That is
authentic — and it is exactly why `Alt+F` fast-forward and `Ctrl+C` flush are in
the v1 spec rather than deferred.

---

## 6. Decision on D2's escape hatch

**No repaint-coalescing hatch. D2 stands unmodified.**

Task 1.7 asked whether D2 needs an escape hatch to coalesce redundant ConPTY
repaints. It does not:

- There is nothing to coalesce at rest — ConPTY sends zero bytes (§2).
- pwsh's per-keystroke cost is not redundant repainting. It is the genuine echo of
  a line editor redrawing the line the user is editing. Dropping intermediate
  states would mean showing a line the shell never sent, which is the exact
  inauthenticity D2 exists to prevent.
- The escape-sequence share (§3) is the *point*, not overhead to be optimised away.

The real mitigations are the ones already specified — readline taming on by
default, per-shell guidance on usable rates — plus client-side line editing as a
post-v1 feature, which is the only thing that would genuinely fix pwsh at 300 baud
and is what real terminals did in half-duplex.

---

## 7. Corrections required

**design.md D4** — replace the `PSEUDOCONSOLE_INHERIT_CURSOR` recommendation with
`dwFlags: 0`, and record the `CREATE_NO_WINDOW` trap.

**design.md D2** — the worked example claims a default `oh-my-posh` prompt costs
~500 bytes and 4.2 s at 1200 baud. Measured default prompts are far cheaper: 21
bytes (pwsh default), 6 (tamed), 17 (cmd). The taming default is still justified,
but by the *keystroke* numbers in §4, not by prompt cost.

**design.md risk list** — downgrade "ConPTY repaint chattiness"; it is measured and
benign. Replace it with the real risk: pwsh is not usable for typing below about
2400 baud, at any level of taming.

**design.md, tasks.md** — target framework is net10.0. .NET 9 reached end of
support in May 2026.

**specs/shell-hosting** — the scenario "typing a character causes fewer than 32
bytes of echo traffic, so input remains usable at 1200 baud" fails on both counts:
measured 38.2 bytes average (57 peak), and 1200 baud is not usable for pwsh
regardless. Amend it to assert what taming actually guarantees — a large reduction
against the untamed baseline — and state the usable-rate floor per shell honestly.

**New emulation requirement** — the emulator must answer DSR (`ESC[6n` →
`ESC[row;colR`) and DA queries from the real cursor position. Already listed in
specs/terminal-emulation, but worth flagging: ConPTY *blocks* awaiting the answer,
so an unanswered query is a hang, not a cosmetic gap.

---

## 8. Fixtures produced

24 captures, 262 KB, in `spike/captures/` — every profile × {idle, keystrokes,
prompt, listing, color, deep-listing, git-status, altbuffer}. Each is a `.bin`
(raw bytes as received, the parser fixture) plus a `.timing` (`micros offset
length` per read).

All three `altbuffer` captures contain a real `ESC[?1049h` / `ESC[?1049l` pair from
`vim -u NONE`, so the alternate-screen requirement has a genuine fixture rather
than a synthetic one.

Note the `.bin` files cover the whole session including startup, while
`measurements.tsv` reports the per-scenario delta after the prompt settled — which
is why, for example, `cmd-idle.bin` is 195 bytes although the measured idle delta
is 0.
