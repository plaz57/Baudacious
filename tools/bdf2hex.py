"""Convert a fixed-cell BDF face to the GNU Unifont .hex format Baudacious reads.

Only the 256 code points CP437 needs are emitted: the atlas draws nothing else, and a
resource carrying a thousand unused glyphs is a resource nobody wants in the binary.

Run once per face; the .hex is committed and the BDF is not.
"""

import io
import re
import sys

# The PC ROM drew glyphs in 0x00-0x1F where the CP437 codec sees control characters, so
# those 32 slots are named here and the rest come from the codec.
LOW = (
    ' ☺☻♥♦♣♠•◘○◙♂♀♪♫☼'
    '►◄↕‼¶§▬↨↑↓→←∟↔▲▼'
)


def cp437_codepoints():
    points = []
    for i in range(256):
        if i < 0x20:
            points.append(ord(LOW[i]))
        else:
            points.append(ord(bytes([i]).decode('cp437')))
    return points


def parse_bdf(path):
    """Every glyph as {codepoint: [16 rows of 8 bits]}, placed in the font's cell."""
    text = io.open(path, encoding='latin-1').read()

    box = re.search(r'^FONTBOUNDINGBOX\s+(-?\d+)\s+(-?\d+)\s+(-?\d+)\s+(-?\d+)', text, re.M)
    if box is None:
        raise SystemExit('no FONTBOUNDINGBOX')

    cell_w, cell_h, _, cell_yoff = (int(g) for g in box.groups())
    if (cell_w, cell_h) != (8, 16):
        raise SystemExit(f'expected an 8x16 cell, found {cell_w}x{cell_h}')

    # BDF draws bottom-up from the baseline; the cell's top row is this y.
    top = cell_h + cell_yoff - 1

    glyphs = {}
    for chunk in text.split('STARTCHAR')[1:]:
        encoding = re.search(r'^ENCODING\s+(-?\d+)', chunk, re.M)
        bbx = re.search(r'^BBX\s+(-?\d+)\s+(-?\d+)\s+(-?\d+)\s+(-?\d+)', chunk, re.M)
        bitmap = re.search(r'^BITMAP\s*$(.*?)^ENDCHAR', chunk, re.M | re.S)

        if encoding is None or bbx is None or bitmap is None:
            continue

        code = int(encoding.group(1))
        if code < 0:
            continue

        w, h, xoff, yoff = (int(g) for g in bbx.groups())
        rows = [int(line.strip(), 16) for line in bitmap.group(1).split() if line.strip()]

        cell = [0] * cell_h
        for i, bits in enumerate(rows[:h]):
            # Glyph row i counts down from the glyph's top; place it in the cell.
            r = top - (yoff + h - 1 - i)
            if not 0 <= r < cell_h:
                continue

            # BDF pads each row to a whole number of bytes, left-aligned in the box.
            padded = (w + 7) // 8 * 8
            value = bits >> (padded - w)          # drop the padding
            value <<= max(0, cell_w - w - xoff)   # then position it in the cell
            cell[r] |= value & 0xFF

        glyphs[code] = cell

    return glyphs


def main():
    source, target, name = sys.argv[1], sys.argv[2], sys.argv[3]
    glyphs = parse_bdf(source)
    wanted = cp437_codepoints()

    lines = []
    missing = []
    for code in sorted(set(wanted)):
        if code not in glyphs:
            missing.append(code)
            continue

        bits = ''.join(f'{row:02X}' for row in glyphs[code])
        lines.append(f'{code:04X}:{bits}')

    io.open(target, 'w', encoding='ascii', newline='\n').write('\n'.join(lines) + '\n')

    print(f'{name}: {len(lines)} of {len(set(wanted))} CP437 code points')
    if missing:
        print('  missing: ' + ' '.join(f'U+{c:04X}' for c in missing))


if __name__ == '__main__':
    main()
