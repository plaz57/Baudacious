# Bundled assets

Three 8x16 bitmap faces, all permissively licensed, selected with `font =` in
`baudacious.ini` or `font <name>` at the local prompt. The full licence texts are in
`licenses/`.

| Name | File | Stems | Licence |
|---|---|---|---|
| `unscii` | `unscii-16.hex` | 2px | public domain |
| `terminus` | `terminus-16.hex` | **1px** | OFL 1.1 |
| `spleen` | `spleen-16.hex` | 2px | BSD 2-Clause |

Stem width is the axis that matters. A two-pixel stem doubles again at 2x
magnification, and four pixels of stem is what "too bold" means at this size. Terminus
is the light one; Spleen is the same weight as unscii with different letterforms, which
is a choice rather than a fix.

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

## terminus-16.hex

`ter-u16n` from Terminus Font, converted from BDF. The light face: capitals have
**one-pixel stems** where unscii and Spleen have two.

- **Source:** <https://terminus-font.sourceforge.net/>
- **Author:** Dimitar Toshkov Zhekov
- **Licence:** SIL Open Font License 1.1, with Reserved Font Name "Terminus Font"
  (`licenses/terminus-OFL-1.1.txt`)
- **Retrieved:** 2026-09-07, `xfonts-terminus_4.49.orig.tar.gz` from the Debian source
  pool at `deb.debian.org`, which is upstream 4.49 unmodified. SourceForge was the
  first choice and returns 404 for the tarball.

The OFL keeps this **file** under the OFL while the application stays MIT; the licence
is explicit that it does not reach the software a font is bundled with. Two conditions
are worth naming: the licence text ships beside the font, and the Reserved Font Name is
"Terminus Font", which Baudacious does not use for the converted file — the setting is
`terminus` and the credit names the original.

## spleen-16.hex

Spleen 8x16, converted from BDF. The same two-pixel weight as unscii, with rounder,
wider letterforms — an alternative rather than a lighter option.

- **Source:** <https://www.cambus.net/spleen-monospaced-bitmap-fonts/>
- **Author:** Frederic Cambus
- **Licence:** BSD 2-Clause (`licenses/spleen-BSD-2-Clause.txt`), which requires the
  copyright notice be reproduced — it is, in the `about` panel and here.
- **Retrieved:** 2026-09-07, `spleen-8x16.bdf` 2.2.0 from
  `raw.githubusercontent.com/fcambus/spleen`.

## The BDF conversion

Terminus and Spleen are published as BDF; `HexFont` reads the GNU Unifont `.hex` format.
`tools/bdf2hex.py` converts one to the other, run once per face, and the `.hex` is
committed while the BDF is not.

It emits only the 256 code points CP437 needs — the atlas draws nothing else, and a
resource carrying a thousand unused glyphs is a resource nobody wants in the binary. The
one subtlety is placement: BDF measures from the baseline and stores each glyph in its
own bounding box, so a glyph has to be positioned within the font's cell rather than
copied into it. The converter does that arithmetic and the comment there explains it.

Coverage after conversion: Terminus 254 of the 255 distinct CP437 code points (it lacks
`⌂` U+2302), Spleen 252 (it lacks `∟` U+221F, `►` U+25BA and `◄` U+25C4). The atlas
fills those the same way it fills unscii's gaps.
