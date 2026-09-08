using System.Globalization;
using Baudacious.App;
using Baudacious.App.Config;
using Baudacious.Core.Baud;
using Baudacious.Core.Commands;
using Baudacious.Render;

// Before anything touches Console: the streams bind on first use, and a diagnostic that
// prints into a console we had not attached to yet prints into nothing.
ParentConsole.Attach();

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
    // --settings decides which files the lower layers come from, so it is found before any
    // of them are read rather than during the parse below.
    string? directory = null;
    for (var i = 0; i + 1 < args.Length; i++)
    {
        if (string.Equals(args[i], "--settings", StringComparison.Ordinal))
        {
            directory = args[i + 1];
        }
    }

    // The settings files are the bottom two layers and the command line goes on top, which
    // is what makes an option a way to override the configuration for one run rather than a
    // second place to configure things.
    var store = ConfigStore.Load(directory);
    var options = store.Settings;

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

            case "--settings" when i + 1 < args.Length:
                i++;   // already read, above
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

                options = options with { Display = options.Display with { Phosphor = phosphor } };
                break;

            case "--scale" when i + 1 < args.Length:
                var scale = Math.Clamp(int.Parse(args[++i], CultureInfo.InvariantCulture), 1, 8);
                options = options with { Display = options.Display with { FontScale = scale } };
                break;

            case "--rich-editor":
                options = options with { FastEcho = false };
                break;

            case "--fast-echo":
                options = options with { FastEcho = true };
                break;

            case "--no-crt":
                options = options with { Display = options.Display with { Crt = CrtSettings.Off } };
                break;

            case "--bloom" when i + 1 < args.Length:
                var bloom = Number(args[++i], "bloom");
                options = Crt(options, c => c with { Bloom = bloom });
                break;

            case "--scanline" when i + 1 < args.Length:
                var scanline = Number(args[++i], "scanline");
                options = Crt(options, c => c with { Scanline = scanline });
                break;

            case "--vignette" when i + 1 < args.Length:
                var vignette = Number(args[++i], "vignette");
                options = Crt(options, c => c with { Vignette = vignette });
                break;

            case "--curvature" when i + 1 < args.Length:
                var curvature = Number(args[++i], "curvature");
                options = Crt(options, c => c with { Curvature = curvature });
                break;

            case "--mask" when i + 1 < args.Length:
                var mask = Number(args[++i], "mask");
                options = Crt(options, c => c with { Mask = mask });
                break;

            case "--persistence" when i + 1 < args.Length:
                var persistence = Number(args[++i], "persistence");
                options = Crt(options, c => c with { Persistence = persistence });
                break;

            case "-h" or "--help":
                return Help();

            default:
                Console.Error.WriteLine($"unrecognised option: {args[i]}");
                return Help();
        }
    }


    return TerminalWindow.Run(options, store);
}

static Settings Crt(Settings settings, Func<CrtSettings, CrtSettings> change) =>
    settings with { Display = settings.Display with { Crt = change(settings.Display.Crt) } };

// Per effect, from the one table that knows: 0 to 1 for the five that scale something,
// 0 to 4 for bloom, which adds light instead. This used to clamp everything to 0-4, which
// let --curvature 3 through; clamping everything to 0-1 instead, as I first "fixed" it,
// silently broke the documented --bloom 2.5. Neither single number was right.
static double Number(string text, string effect) =>
    double.TryParse(text, CultureInfo.InvariantCulture, out var value)
        ? Math.Clamp(value, 0, CrtEffect.CeilingOf(effect))
        : 0;

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

          Every option below overrides the settings file, for this run only.

            -b, --baud <rate>               line rate, or 'unlimited'
            -s, --shell <name>              pwsh, cmd, wsl, wsl:<distribution>,
                                            or a profile from the settings file
            -p, --phosphor <name>           green, amber, white, color
                --scale <n>                 whole-number magnification of the 8x16 cell
                --rich-editor               keep PSReadLine; ~29 bytes per keypress
                --fast-echo                 drop PSReadLine whatever the rate; 1 byte
                                            per keypress, no coloring or prediction
                --no-crt                    skip the CRT pass entirely
                --bloom <n>                 phosphor bleed, 0 to switch off
                --scanline <n>              raster gap depth; needs 2x magnification
                                            or more to be visible at all
                --vignette <n>              corner darkening
                --curvature <n>             screen bulge; 0 is flat glass
                --mask <n>                  aperture-grille stripes; 0 is none
                --persistence <n>           how long light lingers; 0 is none
                --settings <directory>      read baudacious.ini and state.ini from here
                                            instead of %APPDATA%\Baudacious

          In the window:
            Alt+B  line rate menu           Alt+G  framing menu
            Alt+P  phosphor menu            Alt+C  CRT pass on or off
            Alt+F  release the backlog      Alt+Shift+F  10x while held
            Alt+N  another shell            Alt+S  next shell
            Alt+- / Alt+=  font scale       Alt+Enter  fullscreen
            Alt+Z  every binding            Alt+X  quit, twice
            Ctrl+] local commands: baud, phosphor, fastecho, session new, ...
            Ctrl+C interrupt the shell and drop the backlog

          Settings live in %APPDATA%\Baudacious\baudacious.ini, which is written
          with commented defaults on first run and re-read when it changes.

          Diagnostics:
            console [options]               same, painted into the host console
            meter <capture.bin> [options]    replay a recorded stream at a rate
            shot [options]                   render one frame to a PNG
            atlas <out.png> [magnify]        dump the glyph atlas
        """);

    return 1;
}
