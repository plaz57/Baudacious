# Third-party notices

Baudacious is MIT licensed (see [LICENSE](LICENSE)). It uses the components below.
Nothing here is copyleft, and nothing here places a condition on how Baudacious itself
may be licensed or used.

## Bundled in the binary

### unscii-16

An 8×16 bitmap font, embedded as a resource and used for every glyph drawn.

- Author: Viznut — <http://viznut.fi/unscii/>
- **Public domain.** No attribution required; the credit is given because it is deserved.
- This is the plain `unscii-16`, **not** `unscii-16-full`, which incorporates GNU Unifont
  and is GPL. See [assets/README.md](assets/README.md).

## Redistributed with a self-contained build

These ship as assemblies alongside Baudacious. All are MIT, whose only condition is that
the copyright and permission notice travel with the software — which this file satisfies.

| Component | Licence | Project |
|---|---|---|
| Silk.NET.Windowing | MIT | <https://github.com/dotnet/Silk.NET> |
| Silk.NET.OpenGL | MIT | <https://github.com/dotnet/Silk.NET> |
| Silk.NET.Input | MIT | <https://github.com/dotnet/Silk.NET> |
| System.Drawing.Common | MIT | <https://github.com/dotnet/runtime> |

Silk.NET is copyright .NET Foundation and Contributors. System.Drawing.Common is
copyright the .NET Foundation and Contributors.

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
