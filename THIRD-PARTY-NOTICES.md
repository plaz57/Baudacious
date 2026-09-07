# Third-party notices

Baudacious is MIT licensed (see [LICENSE](LICENSE)). It uses the components below.
Nothing here is copyleft, and nothing here places a condition on how Baudacious itself
may be licensed or used.

## Bundled in the binary

Three 8×16 bitmap faces, embedded as resources. One of them draws every glyph; which one
is a setting. Provenance and the BDF conversion are recorded in
[assets/README.md](assets/README.md), and the full licence texts are in
[assets/licenses/](assets/licenses/).

### unscii-16 — the default face

- Author: Viznut — <http://viznut.fi/unscii/>
- **Public domain.** No attribution required; the credit is given because it is deserved.
- This is the plain `unscii-16`, **not** `unscii-16-full`, which incorporates GNU Unifont
  and is GPL.

### Terminus (`ter-u16n`)

- Author: Dimitar Toshkov Zhekov — <https://terminus-font.sourceforge.net/>
- **SIL Open Font License 1.1**, with Reserved Font Name "Terminus Font"
  ([text](assets/licenses/terminus-OFL-1.1.txt)).
- The OFL keeps this **file** under the OFL and is explicit that it does not reach the
  software a font is bundled with, so Baudacious stays MIT. Two conditions apply and both
  are met: the licence text ships with the font, and the converted file is not called by
  the Reserved Font Name — the setting is `terminus` and the credit names the original.

### Spleen 8x16

- Author: Frederic Cambus — <https://www.cambus.net/spleen-monospaced-bitmap-fonts/>
- **BSD 2-Clause** ([text](assets/licenses/spleen-BSD-2-Clause.txt)), whose condition is
  that the copyright notice be reproduced in the documentation. It is, here and in the
  `about` panel.

## Redistributed with a self-contained build

These ship inside the published executable, apart from `glfw3.dll`, which has to sit
beside it. All are MIT, whose only condition is that the copyright and permission notice
travel with the software — which this file satisfies.

| Component | Licence | Project |
|---|---|---|
| Silk.NET.Windowing | MIT | <https://github.com/dotnet/Silk.NET> |
| Silk.NET.OpenGL | MIT | <https://github.com/dotnet/Silk.NET> |
| Silk.NET.Input | MIT | <https://github.com/dotnet/Silk.NET> |
| GLFW (`glfw3.dll`) | Zlib | <https://www.glfw.org/> |
| System.Drawing.Common | MIT | <https://github.com/dotnet/runtime> |

Silk.NET is copyright .NET Foundation and Contributors. System.Drawing.Common is
copyright the .NET Foundation and Contributors. GLFW is copyright Marcus Geelnard and Camilla
Löwy, under the zlib/libpng licence, which is permissive and asks only that the notice not be
removed.

## Build and test only

Not redistributed; they do not ship in any build of Baudacious.

| Component | Licence |
|---|---|
| xunit, xunit.runner.visualstudio | Apache-2.0 |
| Microsoft.NET.Test.Sdk | MIT |
| coverlet.collector | MIT |

## Recorded fixtures

`tests/fixtures/conpty-captures/` holds byte streams recorded from `cmd.exe` and
PowerShell running under Windows ConPTY. They are factual recordings of program output
made for interoperability testing, not redistributions of Microsoft software.
