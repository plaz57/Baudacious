# Contributing

Patches welcome. This file is what you need to get from a clone to a working change, plus
the few conventions that are not obvious from reading the code.

## Building

Windows 10 1809 or later, a GPU with OpenGL 3.3, and the .NET 10 SDK.

```
git clone https://github.com/plaz57/Baudacious.git
cd Baudacious
dotnet build Baudacious.sln -c Release
dotnet test Baudacious.sln -c Release
```

`Baudacious.sln` opens the four libraries, both test projects and the ConPTY spike in
Visual Studio, with the settings files and the specs as solution items.

To run it:

```
dotnet run -c Release --project src/Baudacious.App -- --baud 1200 --shell cmd
```

## How to check your work

Most of this project is only true on a screen, so the tests are necessary and not
sufficient.

- **`dotnet test`** — 480-odd tests, and they should all pass before you open a PR.
- **`baudacious console`** — the whole pipeline (ConPTY, governor, parser, grid) rendered
  into the terminal you launched it from, with no window. The fastest way to check
  anything that is not about pixels. `--script` and `--out` make it scriptable.
- **`baudacious shot`** — renders a screen offscreen to a PNG without opening a window.
  `--overlay`, `--status`, `--magnify` and every CRT flag work. This is how the glyph
  checks and the effect comparisons were done.
- **`baudacious shot --bench`** — frame cost. Worth running if you touch the renderer; the
  budget is 16.67 ms and the whole pass currently costs about 1 ms.

If a change is visible, look at it before sending it. Several bugs in this codebase's
history were invisible to the tests and obvious on screen.

## Conventions worth knowing

**Measure, then state the number.** Most defaults here exist because someone measured
something — 28.5 bytes per keystroke, a four-frame phosphor tail, 0.97 ms a frame. If you
change a default, bring its figure. If you add one, say how you chose it.

**Anything drawn on the grid must be CP437.** The glyph atlas has 256 slots and anything
outside them renders as a solid square. Use a hyphen, never an em dash, in status bar text,
menus, the help overlay or prompt messages. `ChromeGlyphTests` catches some of this but
cannot see interpolated strings.

**The governor meters bytes, not glyphs.** Escape sequences cost wire time exactly as
printable characters do, because that is what a serial line did. Nothing should be able to
skip the queue.

**Comments explain why.** The code says what it does. The interesting part is usually the
constraint that made it that shape, and that is what is worth writing down — especially
when the obvious approach was tried first and failed.

## Specs

Requirements live in `openspec/specs/`, one file per capability, and describe the product
rather than the history. Completed proposals are archived under
`openspec/changes/archive/` with their design notes and measurements intact — those are
often the fastest way to find out why something is the way it is.

A change large enough to alter behaviour someone could depend on should update the
relevant spec in the same pull request.

## Pull requests

`main` is protected: everything lands through a pull request with a review and a green
build. Fork, branch, and open it against `main`.

Small and focused beats large and complete. If you have found three things, three pull
requests are easier to review and quicker to merge than one.
