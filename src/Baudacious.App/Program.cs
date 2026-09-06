using System.Globalization;
using Baudacious.App;
using Baudacious.Core.Baud;
using Baudacious.Render;

// The window is the product; everything else is a diagnostic left over from building it.
return args.Length > 0 && !args[0].StartsWith('-')
    ? args[0].ToLowerInvariant() switch
    {
        "meter" => MeterCommand.Run(args[1..]),
        "console" => ShellCommand.Run(args[1..]),
        "atlas" => AtlasCommand.Run(args[1..]),
        "shot" => ShotCommand.Run(args[1..]),
        "help" => Help(),
        _ => Unknown(args[0]),
    }
    : RunWindow(args);

static int RunWindow(string[] args)
{
    var options = new TerminalWindow.Options();

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--baud" or "-b" when i + 1 < args.Length:
                if (!LineRate.TryParse(args[++i], out var rate))
                {
                    Console.Error.WriteLine($"not a rate: {args[i]}");
                    return 1;
                }

                options = options with { Rate = rate };
                break;

            case "--shell" or "-s" when i + 1 < args.Length:
                options = options with { Profile = args[++i] };
                break;

            case "--phosphor" or "-p" when i + 1 < args.Length:
                if (!Enum.TryParse<Phosphor>(args[++i], ignoreCase: true, out var phosphor))
                {
                    Console.Error.WriteLine($"not a phosphor: {args[i]} (green, amber, white, color)");
                    return 1;
                }

                options = options with { Phosphor = phosphor };
                break;

            case "--scale" when i + 1 < args.Length:
                options = options with
                {
                    FontScale = Math.Clamp(int.Parse(args[++i], CultureInfo.InvariantCulture), 1, 8),
                };
                break;

            case "--rich-editor":
                options = options with { FastEcho = false };
                break;

            case "--no-crt":
                options = options with { Crt = CrtSettings.Off };
                break;

            case "--bloom" when i + 1 < args.Length:
                options = options with { Crt = options.Crt with { Bloom = Number(args[++i]) } };
                break;

            case "--scanline" when i + 1 < args.Length:
                options = options with { Crt = options.Crt with { Scanline = Number(args[++i]) } };
                break;

            case "--vignette" when i + 1 < args.Length:
                options = options with { Crt = options.Crt with { Vignette = Number(args[++i]) } };
                break;

            case "-h" or "--help":
                return Help();

            default:
                Console.Error.WriteLine($"unrecognised option: {args[i]}");
                return Help();
        }
    }

    // Below the threshold the rich line editor stops being an annoyance and starts being
    // unusable, so fast echo becomes the default unless the user asked otherwise.
    if (!options.Rate.IsUnlimited
        && options.Rate.Baud < ShellProfiles.FastEchoThresholdBaud
        && options.FastEcho)
    {
        Console.Error.WriteLine(
            $"{options.Rate}: using fast echo (no PSReadLine) so typing keeps up. --rich-editor to override.");
    }

    return TerminalWindow.Run(options);
}

static double Number(string text) =>
    double.TryParse(text, CultureInfo.InvariantCulture, out var value) ? Math.Clamp(value, 0, 4) : 0;

static int Unknown(string command)
{
    Console.Error.WriteLine($"unknown command: {command}");
    return Help();
}

static int Help()
{
    Console.Error.WriteLine("""
        Baudacious — a terminal with a baud rate.

          baudacious [options]              open the terminal window

            -b, --baud <rate>               line rate, or 'unlimited' (default 1200)
            -s, --shell <pwsh|cmd>          which shell to host
            -p, --phosphor <name>           green (default), amber, white, color
                --scale <n>                 whole-number magnification of the 8x16 cell
                --rich-editor               keep PSReadLine; ~29 bytes per keystroke
                                            instead of 1, so typing lags below 9600 baud
                --no-crt                    skip the CRT pass entirely
                --bloom <n>                 phosphor bleed, 0 to switch off (default 0.55)
                --scanline <n>              raster gap depth (default 0.30); needs 2x
                                            magnification or more to be visible
                --vignette <n>              corner darkening (default 0.35)

          In the window:
            Alt+B  step through the standard line rates
            Alt+F  fast-forward the queued backlog
            Alt+P  cycle the phosphor
            Alt+C  toggle the CRT pass
            Alt+- / Alt+=   font scale
            Alt+X  quit
            Ctrl+C interrupt the shell and drop the backlog

          Diagnostics:
            console [options]               same, painted into the host console
            meter <capture.bin> [options]    replay a recorded stream at a rate
            shot [options]                   render one frame to a PNG
            atlas <out.png> [magnify]        dump the glyph atlas
        """);

    return 1;
}
