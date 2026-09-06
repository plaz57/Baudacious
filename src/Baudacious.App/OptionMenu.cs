using System.Globalization;
using Baudacious.Core.Baud;
using Baudacious.Render;

namespace Baudacious.App;

internal enum MenuKind
{
    None,
    Baud,
    Framing,
    Phosphor,
}

/// <summary>
/// The choices behind Alt+B, Alt+G and Alt+P, and how to draw them.
/// </summary>
/// <remarks>
/// These were cycling keys that announced the result in the status bar, which works but
/// never shows what else there is. A list that says where you are in it and what is on
/// either side answers the question the bar could not.
/// <para>
/// The rate menu changes only the rate and the framing menu only the framing — that is
/// what having two menus means. The convention that 110 baud implies two stop bits still
/// applies where a rate is named from nothing, as on the command line; it does not
/// silently undo a framing the user picked.
/// </para>
/// </remarks>
internal static class OptionMenu
{
    /// <summary>Bits per second on offer, with 0 standing for unlimited, last.</summary>
    public static readonly int[] Bauds = [.. LineRate.StandardBauds, 0];

    /// <summary>
    /// 8N1 and 7E1 are both ten bits per character; 8N2 and 8E1 are eleven, so the same
    /// baud runs measurably slower. The order runs fast to slow for that reason.
    /// </summary>
    public static readonly Framing[] Framings = [Framing.N81, Framing.E71, Framing.N82, Framing.E81];

    public static readonly Phosphor[] Phosphors = [.. Enum.GetValues<Phosphor>()];

    public static int Count(MenuKind kind) => kind switch
    {
        MenuKind.Baud => Bauds.Length,
        MenuKind.Framing => Framings.Length,
        MenuKind.Phosphor => Phosphors.Length,
        _ => 0,
    };

    public static string Title(MenuKind kind) => kind switch
    {
        MenuKind.Baud => "line rate",
        MenuKind.Framing => "framing",
        MenuKind.Phosphor => "phosphor",
        _ => string.Empty,
    };

    /// <summary>The rate a menu position means, keeping the framing already in force.</summary>
    public static LineRate RateAt(int index, Framing framing) =>
        Bauds[index] == 0 ? LineRate.Unlimited : LineRate.Of(Bauds[index], framing);

    /// <summary>
    /// The panel: a title, then every option with the chosen one marked.
    /// </summary>
    /// <remarks>
    /// The mark is a leading arrow rather than reverse video, because the overlay draws
    /// every cell the same way and a second color would mean teaching it to do otherwise
    /// for one line.
    /// </remarks>
    public static string[] Render(MenuKind kind, int selected, LineRate rate, Phosphor phosphor)
    {
        var labels = Labels(kind, rate, phosphor);
        var lines = new string[labels.Length + 2];

        lines[0] = Title(kind);
        for (var i = 0; i < labels.Length; i++)
        {
            lines[i + 1] = (i == selected ? "  > " : "    ") + labels[i];
        }

        lines[^1] = "  up/down to choose, enter or escape to close";
        return lines;
    }

    /// <summary>One label per option, in menu order.</summary>
    public static string[] Labels(MenuKind kind, LineRate rate, Phosphor phosphor) => kind switch
    {
        // Character rates are quoted under the framing actually in force, since that is
        // what the choice would run at.
        MenuKind.Baud => [.. Bauds.Select(b => Describe(b, rate.Framing))],
        MenuKind.Framing => [.. Framings.Select(f => Describe(f, rate))],
        MenuKind.Phosphor => [.. Phosphors.Select(Describe)],
        _ => [],
    };

    /// <summary>Where the running configuration sits in the menu, or 0 if it is off the list.</summary>
    public static int IndexOf(MenuKind kind, LineRate rate, Phosphor phosphor) => kind switch
    {
        MenuKind.Baud => Math.Max(0, Array.IndexOf(Bauds, rate.Baud)),
        MenuKind.Framing => Math.Max(0, Array.IndexOf(Framings, rate.Framing)),
        MenuKind.Phosphor => Math.Max(0, Array.IndexOf(Phosphors, phosphor)),
        _ => 0,
    };

    private static string Describe(int baud, Framing framing) =>
        baud == 0
            ? "unlimited"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{baud,6}   {baud / (double)framing.BitsPerCharacter,7:F1} chars/sec");

    private static string Describe(Framing framing, LineRate rate) =>
        rate.IsUnlimited
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{framing}   {framing.BitsPerCharacter} bits per character")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{framing}   {rate.Baud / (double)framing.BitsPerCharacter,7:F1} chars/sec");

    private static string Describe(Phosphor phosphor) => phosphor switch
    {
        Phosphor.Color => "color    no transform; colors as sent",
        Phosphor.Green => "green    P1",
        Phosphor.Amber => "amber    P3",
        _ => "white    P4",
    };
}
