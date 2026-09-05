using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
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

        if (capture == "blocks")
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

        var settings = new DisplaySettings { Phosphor = phosphor, CursorBlink = false };
        OffscreenRenderer.CaptureToPng(screen, settings, output, magnify);

        Console.Error.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{columns}x{rows} grid rendered at {magnify}x -> {output}"));

        return 0;
    }

    /// <summary>Just the block and shade glyphs, spaced out so each can be seen alone.</summary>
    private static string BlockPattern() =>
        "shade   ░ ▒ ▓ █\r\n\r\n" +
        "halves  ▀ ▄ ▌ ▐\r\n\r\n" +
        "solid   ████  adjacent ▌▐ ▀▄\r\n\r\n" +
        "rule    ────  ════   corners ┌┐└┘ ╔╗╚╝\r\n";
    /// <summary>
    /// Exercises the things that are easy to get wrong and easy to see: box-drawing joins,
    /// the block and shade ramp, colour, and the text attributes.
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

        // Colour: the 16 ANSI colours as background swatches.
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
