using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using Baudacious.App.Config;
using Baudacious.Core.Terminal;
using Baudacious.Render;

namespace Baudacious.App;

/// <summary>
/// Renders a test pattern (or a recorded capture) through the real GL pipeline and writes
/// the frame to a PNG.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ShotCommand
{
    public static int Run(string[] args)
    {
        var output = "shot.png";
        var magnify = 2;
        var phosphor = Phosphor.Green;
        string? capture = null;
        var columns = 80;
        var rows = 25;
        var crt = true;
        var bench = false;
        var overlay = false;
        double? bloom = null, scanline = null, vignette = null, curvature = null;
        var status = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out" or "-o" when i + 1 < args.Length:
                    output = args[++i];
                    break;

                case "--magnify" or "-m" when i + 1 < args.Length:
                    magnify = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;

                case "--phosphor" or "-p" when i + 1 < args.Length:
                    phosphor = Enum.Parse<Phosphor>(args[++i], ignoreCase: true);
                    break;

                case "--bench":
                    bench = true;
                    break;

                case "--status":
                    // Worth having: curvature deliberately spares the status bar, and
                    // without a bar in the frame there is no way to see that offscreen.
                    status = true;
                    break;

                case "--bloom" or "--scanline" or "--vignette" or "--curvature"
                    when i + 1 < args.Length:
                    var knob = args[i];
                    if (!double.TryParse(args[++i], CultureInfo.InvariantCulture, out var level) || level < 0)
                    {
                        Console.Error.WriteLine($"{knob} wants a number from 0 upwards, not '{args[i]}'");
                        return 1;
                    }

                    switch (knob)
                    {
                        case "--bloom": bloom = level; break;
                        case "--scanline": scanline = level; break;
                        case "--curvature": curvature = level; break;
                        default: vignette = level; break;
                    }

                    break;

                case "--overlay":
                    overlay = true;
                    break;

                case "--no-crt":
                    crt = false;
                    break;

                case "--capture" or "-c" when i + 1 < args.Length:
                    capture = args[++i];
                    break;

                case "--size" or "-s" when i + 1 < args.Length:
                    var parts = args[++i].Split('x');
                    columns = int.Parse(parts[0], CultureInfo.InvariantCulture);
                    rows = int.Parse(parts[1], CultureInfo.InvariantCulture);
                    break;

                default:
                    Console.Error.WriteLine($"unrecognised option: {args[i]}");
                    return 1;
            }
        }

        var screen = new Screen(columns, rows);
        var parser = new VtParser(new TerminalEmulator(screen, NullTerminalHost.Instance));

        if (capture == "charset")
        {
            parser.Consume(Encoding.UTF8.GetBytes(CharsetPattern()));
        }
        else if (capture == "blocks")
        {
            parser.Consume(Encoding.UTF8.GetBytes(BlockPattern()));
        }
        else if (capture is not null)
        {
            parser.Consume(File.ReadAllBytes(capture));
        }
        else
        {
            parser.Consume(Encoding.UTF8.GetBytes(TestPattern()));
        }

        var defaults = new CrtSettings();
        var settings = new DisplaySettings
        {
            Phosphor = phosphor,
            CursorBlink = false,
            Crt = crt
                ? defaults with
                {
                    Bloom = bloom ?? defaults.Bloom,
                    Scanline = scanline ?? defaults.Scanline,
                    Vignette = vignette ?? defaults.Vignette,
                    Curvature = curvature ?? defaults.Curvature,
                }
                : CrtSettings.Off,
        };

        if (bench)
        {
            var (crtMs, plainMs) = OffscreenRenderer.Benchmark(screen, settings, magnify, frames: 300);
            var budget = 1000.0 / 60.0;

            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"{columns}x{rows} grid at {magnify}x ({columns * 8 * magnify}x{rows * 16 * magnify} px)"));
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  CRT pass on : {crtMs:F3} ms/frame ({budget / crtMs:F1}x the 60 Hz budget)"));
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  CRT pass off: {plainMs:F3} ms/frame ({budget / plainMs:F1}x)"));
            Console.Error.WriteLine(string.Create(
                CultureInfo.InvariantCulture,
                $"  CRT costs   : {crtMs - plainMs:F3} ms/frame"));

            return 0;
        }
        OffscreenRenderer.CaptureToPng(
            screen,
            settings,
            output,
            magnify,
            status: status
                ? " 1200 8N1  [idle]  alt+B rate  alt+F flush  ctrl+] commands"
                : null,
            overlay: overlay ? Help.For(ConfigStore.DefaultDirectory()) : null);

        Console.Error.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{columns}x{rows} grid rendered at {magnify}x -> {output}"));

        return 0;
    }

    /// <summary>
    /// Every CP437 glyph, plus the pairs that are easiest to confuse.
    /// </summary>
    /// <remarks>
    /// The acceptance test for the CRT pass: bloom that makes the display look right but
    /// renders O and 0 or l and 1 indistinguishable has failed, however good it looks.
    /// </remarks>
    private static string CharsetPattern()
    {
        var sb = new StringBuilder();
        sb.Append("full CP437 repertoire\r\n\r\n");

        for (var row = 0; row < 16; row++)
        {
            sb.Append("  ");
            for (var column = 0; column < 16; column++)
            {
                var slot = (row * 16) + column;
                // NUL, space and NBSP are blank by design; show a dot so the cell is visible.
                sb.Append(slot is 0x00 or 0x20 or 0xFF ? '.' : Baudacious.Render.Cp437.ToUnicode[slot]);
                sb.Append(' ');
            }

            sb.Append("\r\n");
        }

        sb.Append("\r\n  confusable: ILil1|! O0QoD S5 B8 G6 rn/m cl/d vv/w ,.;: ´`'\r\n");
        return sb.ToString();
    }

    /// <summary>Just the block and shade glyphs, spaced out so each can be seen alone.</summary>
    private static string BlockPattern() =>
        "shade   ░ ▒ ▓ █\r\n\r\n" +
        "halves  ▀ ▄ ▌ ▐\r\n\r\n" +
        "solid   ████  adjacent ▌▐ ▀▄\r\n\r\n" +
        "rule    ────  ════   corners ┌┐└┘ ╔╗╚╝\r\n";
    /// <summary>
    /// Exercises the things that are easy to get wrong and easy to see: box-drawing joins,
    /// the block and shade ramp, color, and the text attributes.
    /// </summary>
    private static string TestPattern()
    {
        const string Esc = "\u001b";
        var sb = new StringBuilder();

        sb.Append(Esc).Append("[2J").Append(Esc).Append("[H");
        sb.Append("BAUDACIOUS - glyph and renderer check\r\n\r\n");

        // A closed box: every corner and tee has to meet exactly or it shows immediately.
        sb.Append("  ┌──────────┬──────────┐   ╔══════════╦══════════╗\r\n");
        sb.Append("  │ single   │ box      │   ║ double   ║ box      ║\r\n");
        sb.Append("  ├──────────┼──────────┤   ╠══════════╬══════════╣\r\n");
        sb.Append("  │ joins    │ cleanly  │   ║ joins    ║ cleanly  ║\r\n");
        sb.Append("  └──────────┴──────────┘   ╚══════════╩══════════╝\r\n\r\n");

        sb.Append("  shades ░▒▓█   halves ▀▄▌▐   misc ■·∙√ⁿ²  arrows ←↑→↓ ↔↕\r\n");
        sb.Append("  accents ÇüéâäàåçêëèïîìÄÅ  greek αßΓπΣσµτΦΘΩδ∞φε∩\r\n\r\n");

        // Color: the 16 ANSI colors as background swatches.
        sb.Append("  ");
        for (var i = 0; i < 8; i++)
        {
            sb.Append(Esc).Append(CultureInfo.InvariantCulture, $"[4{i}m  ");
        }

        for (var i = 0; i < 8; i++)
        {
            sb.Append(Esc).Append(CultureInfo.InvariantCulture, $"[10{i}m  ");
        }

        sb.Append(Esc).Append("[0m\r\n\r\n");

        sb.Append("  ").Append(Esc).Append("[1mbold").Append(Esc).Append("[0m  ");
        sb.Append(Esc).Append("[2mfaint").Append(Esc).Append("[0m  ");
        sb.Append(Esc).Append("[4munderline").Append(Esc).Append("[0m  ");
        sb.Append(Esc).Append("[7mreverse").Append(Esc).Append("[0m  ");
        sb.Append(Esc).Append("[9mstruck").Append(Esc).Append("[0m\r\n\r\n");

        sb.Append("  0123456789 the quick brown fox jumps over the lazy dog\r\n");
        sb.Append("  ILil1| O0Qo {}[]()<> !@#$%^&*_-+=~`'\",.;:?/\\\r\n\r\n");
        sb.Append("  cursor is here -> ");

        return sb.ToString();
    }
}
