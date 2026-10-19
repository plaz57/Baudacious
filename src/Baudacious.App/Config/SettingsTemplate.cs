using System.Globalization;
using System.Text;
using Baudacious.Render;

namespace Baudacious.App.Config;

/// <summary>
/// The commented file written on first run.
/// </summary>
/// <remarks>
/// Generated from <see cref="Settings.Defaults"/> and <see cref="KeyBindings.Default"/>
/// rather than typed out, so the file can never claim a default the application does not
/// actually have. Every setting appears, commented out is not an option: a setting the user
/// cannot see is a setting they will never find.
/// </remarks>
public static class SettingsTemplate
{
    public static string Build()
    {
        var d = Settings.Defaults;
        var c = d.Display.Crt;
        var builder = new StringBuilder();

        void Line(string text = "") => builder.Append(text).Append('\n');

        Line("; Baudacious settings. Edit and save; changes apply within a second.");
        Line("; Delete a line to stop having an opinion about it: the last session's");
        Line("; setting applies, or the default if there is none. Delete the file to");
        Line("; have this one written again.");
        Line(";");
        Line("; A setting left untouched here follows what the last session saved in");
        Line("; state.ini. Change one and this file owns it again. An option on the");
        Line("; command line wins over both, for that run only.");
        Line();

        Line("[line]");
        Line("; Bits per second: 110 to 115200, or 'unlimited' for no metering at all.");
        Line("; Any number in that range works, standard or not.");
        Line(Number("baud", d.Rate.Baud));
        Line();
        Line("; Bits per character, which is what turns a bit rate into a character rate.");
        Line("; 8N1 and 7E1 are ten bits; 8N2 and 8E1 are eleven, so the same baud is slower.");
        Line($"framing = {d.Rate.Framing}");
        Line();

        Line("[shell]");
        Line("; pwsh, cmd, wsl, wsl:<distribution>, or a [profile.<name>] defined below.");
        Line($"default = {d.Profile}");
        Line();
        Line("; Swap PowerShell's line editor for the console's own cooked input.");
        Line("; It costs 1 byte per keypress instead of about 29, at the price of syntax");
        Line("; coloring, prediction and ctrl+R history search. Tab completion survives.");
        Line($"; auto lets the rate decide, turning it on below {ShellProfiles.FastEchoThresholdBaud} baud.");
        Line("fast-echo = auto");
        Line();
        Line("; Whether PowerShell runs your own profile script, and so whether your");
        Line("; aliases, functions and prompt exist in here. On, because a terminal that");
        Line("; skips it is not your shell. Turn it off if your profile prints a banner");
        Line("; or builds a themed prompt: at 300 baud that is seconds of crawl before");
        Line("; every new session.");
        Line($"powershell-profile = {Bool(d.PowerShellProfile)}");
        Line();

        Line("[display]");
        Line($"; {string.Join(", ", SettingsReader.PhosphorNames)}. 'color' means no transform at all.");
        Line($"phosphor = {d.Display.Phosphor.ToString().ToLowerInvariant()}");
        Line();
        Line($"; Bitmap typeface: {string.Join(", ", FontCatalog.Names)}. All 8x16,");
        Line("; all permissively licensed; 'about' at the local prompt says whose each");
        Line("; one is. terminus draws one-pixel stems where the others draw two, so it");
        Line("; reads lighter at every magnification.");
        Line($"font = {d.Font}");
        Line();
        Line("; Whole-number magnification of the 8x16 cell. Never fractional: a bitmap face");
        Line("; scaled by halves is a blurry bitmap face.");
        Line(Number("font-scale", d.Display.FontScale));
        Line();
        Line($"; {string.Join(", ", SettingsReader.CursorNames)}.");
        Line($"cursor = {d.Display.CursorShape.ToString().ToLowerInvariant()}");
        Line();
        Line("; Milliseconds between cursor blinks; 0 for a steady cursor.");
        Line(Number("cursor-blink", (int)d.Display.CursorBlinkInterval.TotalMilliseconds));
        Line();
        Line("; Lines of history kept per session.");
        Line(Number("scrollback", d.Scrollback));
        Line();

        Line("[crt]");
        Line("; The tube. Each effect is an intensity from 0 to 1 so they can be judged");
        Line("; separately; enabled = false skips the whole pass.");
        Line($"enabled = {Bool(c.Enabled)}");
        Line();
        Line("; Phosphor bleed around lit pixels.");
        Line(Number("bloom", c.Bloom));
        Line();
        Line("; Depth of the dark gap between raster lines. Needs font-scale 2 or more to");
        Line("; show at all: at 1x every output row samples the same phase.");
        Line(Number("scanline", c.Scanline));
        Line();
        Line("; Darkening towards the corners.");
        Line(Number("vignette", c.Vignette));
        Line();
        Line("; How much the glass bulges toward you. The text area only: the status bar");
        Line("; is chrome, and bending the one line that has to stay legible is a poor");
        Line("; trade. At 1.0 it reads as a fisheye and the corners fall off the glass.");
        Line(Number("curvature", c.Curvature));
        Line();
        Line("; Aperture-grille stripes: output pixels weighted into repeating red,");
        Line("; green and blue columns. A colour-tube artifact — a monochrome tube had a");
        Line("; continuous coating — so it pairs with phosphor = color and is a stylistic");
        Line("; choice on the others. Off by default.");
        Line(Number("mask", c.Mask));
        Line();
        Line("; How long light lingers after the pixel that made it has gone. The one");
        Line("; effect a baud rate makes worth having: at 300 baud characters arrive one");
        Line("; at a time, so the trail follows the cursor as the text is written.");
        Line(Number("persistence", c.Persistence));
        Line();
        Line("; A tube in a lit room is never quite black.");
        Line(Number("ambient-glow", c.AmbientGlow));
        Line();

        Line("[window]");
        Line("; Opening size in cells, not pixels, so it means the same at every font scale.");
        Line(Number("columns", d.Window.Columns));
        Line(Number("rows", d.Window.Rows));
        Line($"fullscreen = {Bool(d.Window.Fullscreen)}");
        Line();

        Line("[keys]");
        Line("; alt, ctrl and shift, then a key: alt+B, alt+shift+F, ctrl+]. Case does not");
        Line("; matter and neither does the order of the modifiers. 'none' unbinds an action,");
        Line("; which then goes to the shell like any other chord.");

        foreach (var action in KeyBindings.Actions)
        {
            var name = KeyBindings.NameOf(action);
            Line($"{name,-13}= {KeyBindings.Default[action],-12}; {KeyBindings.Explain(action)}");
        }

        Line();
        Line("; Extra shells. The name after the dot is what --shell and 'session new' take.");
        Line("; 'like = pwsh' borrows PowerShell's fast-echo handling for another PowerShell;");
        Line("; leave it out for anything else.");
        Line(";");
        Line("; [profile.gitbash]");
        Line("; command = C:\\Program Files\\Git\\bin\\bash.exe");
        Line("; arguments = --login -i");

        return builder.ToString();
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static string Number(string key, double value) =>
        string.Create(CultureInfo.InvariantCulture, $"{key} = {value:0.00}");

    private static string Number(string key, int value) =>
        string.Create(CultureInfo.InvariantCulture, $"{key} = {value}");
}
