# Reporting a vulnerability

Please report security issues through
[GitHub's private advisory form](https://github.com/plaz57/Baudacious/security/advisories/new)
rather than opening a public issue.

This is a hobby project maintained by one person, so I cannot promise a response time. I
will acknowledge what I can reproduce, say plainly when I do not intend to fix something,
and credit you in the advisory unless you would rather I did not.

## What is in scope

Baudacious hosts a shell, so a great deal of what looks alarming is the point: it runs the
program you tell it to, with your privileges. The interesting attack surface is narrower
than that, and it is mostly about **what a hosted program can make the terminal do**:

- Escape sequences from the shell causing Baudacious to execute, write or send something
  the user did not ask for.
- Anything that lets a remote-sourced byte stream — the output of `ssh`, `curl`, `cat` on
  an untrusted file — reach beyond the screen.
- Clipboard, configuration files or key bindings being changed by terminal output rather
  than by the user.
- Memory safety in the parser or the renderer. Both run on bytes the user did not write.

## What is not

- The hosted shell doing what shells do. If `baudacious --shell cmd` runs a command, that
  is the feature.
- Needing to trust a configuration file you edited yourself, or a shell profile you added.
- The missing code signature. Baudacious is not signed and the releases say so; a
  self-signed certificate would be worse than none, because Windows would report a broken
  signature rather than no signature. Releases carry
  [build provenance attestations](https://github.com/plaz57/Baudacious/releases) instead,
  which can be verified with `gh attestation verify`.

## What has already been looked at

[SECURITY-REVIEW.md](SECURITY-REVIEW.md) is a review of the whole repository, including the
terminal-specific surface above and the hardening items still open. Worth reading before
reporting, so you know what is already known.

## Versions

The latest release is the only supported one.
