# Bundled assets

## unscii-16.hex

An 8×16 bitmap font, embedded into `Baudacious.Render` and used for every glyph the
terminal draws.

- **Source:** <http://viznut.fi/unscii/>
- **Author:** Viznut
- **Licence:** **Public domain**
- **Retrieved:** 2026-09-05, from `http://viznut.fi/unscii/unscii-16.hex`

The upstream page states: *"unscii-16-full falls under GPL because of how Unifont is
licensed; the other variants are in the Public Domain."*

**This is the plain `unscii-16`, not `unscii-16-full`.** That distinction is the whole
licence: the `-full` variant incorporates GNU Unifont glyphs and is therefore GPL, which
would reach the whole application. The plain variant is public domain, so it carries no
attribution requirement, no share-alike, and nothing to reconcile if Baudacious is ever
licensed permissively. Do not swap in `-full` without deciding to take the GPL with it.

The credit above is given because it is deserved, not because it is required.

### Why a bitmap font

An outline typeface rasterised down to eight pixels wide has no room to make good
decisions — stems land between pixels and come out as grey mush. A bitmap face has every
pixel placed by hand. It also draws box-drawing characters that actually join, because
they were designed for this exact cell, so tables have no seams at the corners.

### Format

GNU Unifont `.hex`: one glyph per line, `CODEPOINT:HEXBYTES`, row-major, most significant
bit leftmost. Thirty-two hex characters is an 8×16 glyph; sixty-four is a double-width
16×16 one, which Baudacious does not render and skips. Parsed by
`Baudacious.Render.HexFont`.

### Coverage

unscii-16 provides 250 of the 256 CP437 slots. Of the remainder:

- `∙` U+2219 and `⌐` U+2310 are derived from glyphs the font does have — `•` and a
  mirrored `¬` — so they stay consistent with everything around them.
- `⌂` U+2302 and `☼` U+263C are rasterised from a system monospace face and collapsed to
  1-bit, so they sit beside the bitmap glyphs without looking blurred.
