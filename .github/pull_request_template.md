<!--
Thanks for this. Nothing here is a hoop to jump through: each line is something a
reviewer would otherwise have to work out for themselves.
-->

## What this changes

<!-- One or two sentences. What is different afterwards? -->

## Why

<!-- The reasoning, not the restatement. If it fixes something, what was the symptom? -->

## How it was checked

<!--
Say what you actually ran. "Tests pass" is fine if they do; "I watched it at 300 baud
and the trail follows the cursor" is better, because most of this project's behaviour
only shows up on screen.

If you measured something, give the number. The defaults in this codebase are what they
are because someone measured them, and a change to one should come with its own figure.
-->

- [ ] `dotnet test Baudacious.sln -c Release` passes
- [ ] Looked at it running, if the change is visible

## Anything you are unsure about

<!--
Genuinely useful. A reviewer would rather read "I guessed at the default here" than
discover it later.
-->
