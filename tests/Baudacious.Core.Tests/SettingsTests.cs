using Baudacious.App.Config;
using Baudacious.Core.Baud;
using Baudacious.Core.Config;
using Baudacious.Render;

namespace Baudacious.Core.Tests;

public class SettingsReaderTests
{
    private static (Settings Settings, List<string> Problems) Read(string ini, Settings? baseline = null)
    {
        var problems = new List<string>();
        var settings = SettingsReader.Apply(
            baseline ?? Settings.Defaults, IniDocument.Parse(ini), "test.ini", problems);

        return (settings, problems);
    }

    [Fact]
    public void AnEmptyFileChangesNothing()
    {
        var (settings, problems) = Read(string.Empty);

        Assert.Empty(problems);
        Assert.Equal(Settings.Defaults, settings);
    }

    [Fact]
    public void ASettingTakesEffect()
    {
        var (settings, problems) = Read("[line]\nbaud = 2400\n");

        Assert.Empty(problems);
        Assert.Equal(2400, settings.Rate.Baud);
    }

    [Fact]
    public void RateAndFramingAreIndependent()
    {
        var (settings, _) = Read("[line]\nbaud = 2400\nframing = 8N2\n");

        Assert.Equal(2400, settings.Rate.Baud);
        Assert.Equal(Framing.N82, settings.Rate.Framing);
    }

    [Fact]
    public void ABareRateKeepsTheFramingAlreadyInForce()
    {
        // 110 conventionally implies two stop bits, but a file that only changes the rate
        // should not silently undo a framing chosen elsewhere.
        var baseline = Settings.Defaults with { Rate = LineRate.Of(1200, Framing.E71) };
        var (settings, _) = Read("[line]\nbaud = 110\n", baseline);

        Assert.Equal(110, settings.Rate.Baud);
        Assert.Equal(Framing.E71, settings.Rate.Framing);
    }

    [Fact]
    public void AFramingWrittenBesideTheRateWins()
    {
        var baseline = Settings.Defaults with { Rate = LineRate.Of(1200, Framing.E71) };
        var (settings, _) = Read("[line]\nbaud = 2400 8N2\n", baseline);

        Assert.Equal(Framing.N82, settings.Rate.Framing);
    }

    [Fact]
    public void UnlimitedIsARate()
    {
        var (settings, problems) = Read("[line]\nbaud = unlimited\n");

        Assert.Empty(problems);
        Assert.True(settings.Rate.IsUnlimited);
    }

    [Fact]
    public void OneInvalidValueCostsOnlyThatValue()
    {
        var (settings, problems) = Read("[line]\nbaud = quickly\nframing = 7E1\n");

        // The bad one falls back, the good one is honoured, and the problem names both the
        // value and where it is.
        Assert.Equal(Settings.Defaults.Rate.Baud, settings.Rate.Baud);
        Assert.Equal(Framing.E71, settings.Rate.Framing);

        var problem = Assert.Single(problems);
        Assert.Contains("quickly", problem, StringComparison.Ordinal);
        Assert.Contains("line 2", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void AProblemPointsAtTheLineItIsOn()
    {
        var (_, problems) = Read("; a comment\n\n[display]\nfont-scale = enormous\n");

        Assert.Contains("line 4", Assert.Single(problems), StringComparison.Ordinal);
    }

    [Fact]
    public void ANumberOutsideItsRangeIsRefusedRatherThanClamped()
    {
        var (settings, problems) = Read("[display]\nfont-scale = 20\n");

        Assert.Equal(Settings.Defaults.Display.FontScale, settings.Display.FontScale);
        Assert.Contains("outside 1 to 8", Assert.Single(problems), StringComparison.Ordinal);
    }

    [Fact]
    public void IntensitiesAreZeroToOne()
    {
        var (settings, problems) = Read("[crt]\nbloom = 0.2\nvignette = 4\n");

        Assert.Equal(0.2, settings.Display.Crt.Bloom);
        Assert.Equal(Settings.Defaults.Display.Crt.Vignette, settings.Display.Crt.Vignette);
        Assert.Contains("outside 0 to 1", Assert.Single(problems), StringComparison.Ordinal);
    }

    [Fact]
    public void NumbersAreReadTheInvariantWay()
    {
        // The tree builds with InvariantGlobalization, so the ambient culture cannot be
        // changed to test this from the outside. What can be asserted is the parsing
        // itself: a dot is the decimal point and a comma is not a number, whatever a
        // locale might otherwise say.
        var (settings, problems) = Read("[crt]\nbloom = 0.55\nvignette = 0,25\n");

        Assert.Equal(0.55, settings.Display.Crt.Bloom);
        Assert.Equal(Settings.Defaults.Display.Crt.Vignette, settings.Display.Crt.Vignette);
        Assert.Contains("not a number", Assert.Single(problems), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("green", Phosphor.Green)]
    [InlineData("AMBER", Phosphor.Amber)]
    [InlineData("color", Phosphor.Color)]
    public void PhosphorsAreNamedAndCaseInsensitive(string text, Phosphor expected)
    {
        var (settings, problems) = Read($"[display]\nphosphor = {text}\n");

        Assert.Empty(problems);
        Assert.Equal(expected, settings.Display.Phosphor);
    }

    [Fact]
    public void AnUnknownChoiceListsTheRealOnes()
    {
        var (_, problems) = Read("[display]\nphosphor = puce\n");

        Assert.Contains("green", Assert.Single(problems), StringComparison.Ordinal);
    }

    [Fact]
    public void ASteadyCursorIsZeroBlink()
    {
        var (settings, problems) = Read("[display]\ncursor-blink = 0\n");

        Assert.Empty(problems);
        Assert.False(settings.Display.CursorBlink);

        // The interval is kept, so turning blink back on restores the rate it had.
        Assert.Equal(Settings.Defaults.Display.CursorBlinkInterval, settings.Display.CursorBlinkInterval);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("ON")]
    [InlineData("1")]
    public void FlagsAcceptTheObviousSpellings(string text)
    {
        var (settings, problems) = Read($"[window]\nfullscreen = {text}\n");

        Assert.Empty(problems);
        Assert.True(settings.Window.Fullscreen);
    }

    [Fact]
    public void AFlagThatIsNeitherIsReported()
    {
        var (settings, problems) = Read("[crt]\nenabled = perhaps\n");

        Assert.Equal(Settings.Defaults.Display.Crt.Enabled, settings.Display.Crt.Enabled);
        Assert.Contains("true or false", Assert.Single(problems), StringComparison.Ordinal);
    }

    [Fact]
    public void FastEchoHasThreeStates()
    {
        Assert.Null(Read("[shell]\nfast-echo = auto\n").Settings.FastEcho);
        Assert.True(Read("[shell]\nfast-echo = on\n").Settings.FastEcho);
        Assert.False(Read("[shell]\nfast-echo = off\n").Settings.FastEcho);

        var (settings, problems) = Read("[shell]\nfast-echo = maybe\n");
        Assert.Null(settings.FastEcho);
        Assert.Contains("auto, on or off", Assert.Single(problems), StringComparison.Ordinal);
    }

    [Fact]
    public void KeyBindingsAreConfigurable()
    {
        var (settings, problems) = Read("[keys]\nline-rate = ctrl+shift+R\n");

        Assert.Empty(problems);
        Assert.Equal(LocalAction.LineRate, settings.Keys.Find(Silk.NET.Input.Key.R, false, true, true));

        // And the default no longer fires.
        Assert.Null(settings.Keys.Find(Silk.NET.Input.Key.B, true, false, false));
    }

    [Fact]
    public void ABindingCanBeRemoved()
    {
        var (settings, problems) = Read("[keys]\nquit = none\n");

        Assert.Empty(problems);
        Assert.True(settings.Keys[LocalAction.Quit].IsNone);
    }

    [Fact]
    public void AnUnparseableBindingIsReported()
    {
        var (settings, problems) = Read("[keys]\nquit = hyper+Q\n");

        Assert.Equal(KeyBindings.Default[LocalAction.Quit], settings.Keys[LocalAction.Quit]);
        Assert.Contains("key chord", Assert.Single(problems), StringComparison.Ordinal);
    }

    [Fact]
    public void TwoActionsOnOneChordIsReportedRatherThanResolved()
    {
        // Which one should lose is the user's call; dropping one silently would be worse.
        var (_, problems) = Read("[keys]\nquit = alt+B\n");

        Assert.Contains("both on alt+B", Assert.Single(problems), StringComparison.Ordinal);
    }

    [Fact]
    public void ExtraProfilesAreRead()
    {
        var (settings, problems) = Read(
            "[profile.gitbash]\ncommand = C:\\Git\\bin\\bash.exe\narguments = --login -i\n");

        Assert.Empty(problems);

        var profile = Assert.Single(settings.Profiles);
        Assert.Equal("gitbash", profile.Name);
        Assert.Equal("C:\\Git\\bin\\bash.exe", profile.Command);
        Assert.Equal("--login -i", profile.Arguments);
        Assert.Null(profile.Like);
    }

    [Fact]
    public void AProfileWithoutACommandIsRefused()
    {
        var (settings, problems) = Read("[profile.broken]\narguments = -x\n");

        Assert.Empty(settings.Profiles);
        Assert.Contains("needs a 'command'", Assert.Single(problems), StringComparison.Ordinal);
    }

    [Fact]
    public void LayersOnlyReplaceWhatTheySay()
    {
        // This is the whole precedence rule: state over config over defaults, expressed as
        // the order the layers are applied in.
        var problems = new List<string>();

        var config = SettingsReader.Apply(
            Settings.Defaults,
            IniDocument.Parse("[line]\nbaud = 300\n[display]\nphosphor = amber\n"),
            "baudacious.ini",
            problems);

        var state = SettingsReader.Apply(
            config, IniDocument.Parse("[line]\nbaud = 9600\n"), "state.ini", problems);

        Assert.Empty(problems);
        Assert.Equal(9600, state.Rate.Baud);              // state wins
        Assert.Equal(Phosphor.Amber, state.Display.Phosphor);   // config survives
        Assert.Equal(Settings.Defaults.Scrollback, state.Scrollback);   // default survives
    }
}

public class KeyChordTests
{
    [Theory]
    [InlineData("alt+B", Silk.NET.Input.Key.B, true, false, false)]
    [InlineData("ALT+b", Silk.NET.Input.Key.B, true, false, false)]
    [InlineData("alt+shift+F", Silk.NET.Input.Key.F, true, false, true)]
    [InlineData("shift+alt+F", Silk.NET.Input.Key.F, true, false, true)]
    [InlineData("ctrl+]", Silk.NET.Input.Key.RightBracket, false, true, false)]
    [InlineData("control+]", Silk.NET.Input.Key.RightBracket, false, true, false)]
    [InlineData("alt+enter", Silk.NET.Input.Key.Enter, true, false, false)]
    [InlineData("alt+equals", Silk.NET.Input.Key.Equal, true, false, false)]
    [InlineData("alt+minus", Silk.NET.Input.Key.Minus, true, false, false)]
    [InlineData("F5", Silk.NET.Input.Key.F5, false, false, false)]
    [InlineData("alt+f12", Silk.NET.Input.Key.F12, true, false, false)]
    [InlineData("alt+7", Silk.NET.Input.Key.Number7, true, false, false)]
    public void ChordsParse(string text, Silk.NET.Input.Key key, bool alt, bool control, bool shift)
    {
        Assert.True(KeyChord.TryParse(text, out var chord));
        Assert.Equal(new KeyChord(key, alt, control, shift), chord);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hyper+Q")]
    [InlineData("alt+")]
    [InlineData("alt+nonesuch")]
    [InlineData("F13")]
    public void NonsenseDoesNotParse(string text)
    {
        Assert.False(KeyChord.TryParse(text, out _));
    }

    [Fact]
    public void ShiftIsPartOfTheChordRatherThanADifferentKey()
    {
        Assert.True(KeyChord.TryParse("alt+F", out var plain));
        Assert.True(KeyChord.TryParse("alt+shift+F", out var shifted));

        Assert.NotEqual(plain, shifted);
        Assert.False(plain.Matches(Silk.NET.Input.Key.F, alt: true, control: false, shift: true));
        Assert.True(shifted.Matches(Silk.NET.Input.Key.F, alt: true, control: false, shift: true));
    }

    [Fact]
    public void ChordsRoundTripThroughTheirText()
    {
        foreach (var action in KeyBindings.Actions)
        {
            var text = KeyBindings.DefaultChordOf(action);

            Assert.True(KeyChord.TryParse(text, out var chord), text);
            Assert.True(KeyChord.TryParse(chord.ToString(), out var again), chord.ToString());
            Assert.Equal(chord, again);
        }
    }

    [Fact]
    public void NothingMatchesTheEmptyChord()
    {
        Assert.False(KeyChord.None.Matches(Silk.NET.Input.Key.Unknown, false, false, false));
        Assert.True(KeyChord.None.IsNone);
    }
}

public class SettingsTemplateTests
{
    [Fact]
    public void TheGeneratedFileReadsBackAsTheDefaults()
    {
        // The file the user is handed on first run has to mean what the application already
        // does; generating it from the defaults is only half of that guarantee.
        var problems = new List<string>();
        var settings = SettingsReader.Apply(
            Settings.Defaults,
            IniDocument.Parse(SettingsTemplate.Build()),
            "baudacious.ini",
            problems);

        Assert.Empty(problems);
        Assert.Equal(Settings.Defaults, settings);
    }

    [Fact]
    public void TheGeneratedFileParsesCleanly()
    {
        Assert.Empty(IniDocument.Parse(SettingsTemplate.Build()).Problems);
    }

    [Fact]
    public void EverySettingAppearsInIt()
    {
        var ini = IniDocument.Parse(SettingsTemplate.Build());

        Assert.Equal(["line", "shell", "display", "crt", "window", "keys"], ini.Sections());

        Assert.Equal(["baud", "framing"], ini.Keys("line"));
        Assert.Equal(["default", "fast-echo"], ini.Keys("shell"));
        Assert.Equal(
            ["phosphor", "font", "font-scale", "cursor", "cursor-blink", "scrollback"],
            ini.Keys("display"));
        Assert.Equal(
            ["enabled", "bloom", "scanline", "vignette", "ambient-glow"],
            ini.Keys("crt"));
        Assert.Equal(["columns", "rows", "fullscreen"], ini.Keys("window"));
    }

    [Fact]
    public void EveryActionIsListedUnderKeys()
    {
        var ini = IniDocument.Parse(SettingsTemplate.Build());

        foreach (var action in KeyBindings.Actions)
        {
            var name = KeyBindings.NameOf(action);
            Assert.Equal(KeyBindings.DefaultChordOf(action), ini.Get("keys", name));
        }
    }
}
