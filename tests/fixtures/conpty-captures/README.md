# ConPTY capture fixtures

Recorded byte streams from real `pwsh` and `cmd.exe` sessions hosted through
ConPTY, captured by the Phase 0 spike on 2026-09-04. See
`openspec/changes/baudacious-v1/phase-0-findings.md` for the measurements these
came from.

## What each file is

- `<profile>-<scenario>.bin` — the raw bytes ConPTY delivered, in order, exactly
  as received. **This is the parser fixture.**
- `<profile>-<scenario>.timing` — `micros offset length` per `ReadFile`, plus
  `#` comment lines marking session events. Arrival cadence only; it carries no
  bytes of its own.
- `measurements.tsv` — the per-scenario byte counts, measured after the prompt
  had settled.

Profiles are `pwsh-default` (profile loaded, predictions and syntax coloring
on), `pwsh-tamed` (the taming from `specs/shell-hosting`), and `cmd`.

Scenarios are `idle`, `keystrokes`, `prompt`, `listing`, `color`,
`deep-listing`, `git-status`, `altbuffer`.

## What they are used for

- **Phase 1** — input to the governor harness, so metering can be demonstrated
  against a real stream rather than synthetic data.
- **Phase 2** — snapshot tests for the VT parser, and the invariant test that
  matters most: feeding a stream one byte at a time must produce a grid
  identical to feeding it all at once. Every capture is run through both.

All three `altbuffer` captures contain a genuine `ESC[?1049h` / `ESC[?1049l`
pair from `vim -u NONE`, so alternate-screen handling has a real fixture.

## Caveats

The `.bin` files cover the whole session including ConPTY startup, while
`measurements.tsv` reports only the post-settle delta for the scenario. That is
why, for example, `cmd-idle.bin` is 195 bytes although the measured idle delta
is 0.

These are recordings of **ConPTY's output**, not the shells'. ConPTY re-encodes
what the shell wrote into its own screen buffer, so byte counts and sequence
choices are ConPTY's. That is the correct thing to test against, since it is
what Baudacious will actually receive.

## Regenerating

Requires a real launch context — `Start-Process`, not a piped harness (see the
findings document, §1). From the repository root:

```
dotnet build -c Release spike/Baudacious.ConPtySpike
```

then run the built executable with `record <output-dir>`, optionally with
`BAUDACIOUS_GIT_FIXTURE` pointing at a dirty git repository and `BAUDACIOUS_VIM` at a
`vim.exe`.
