using Baudacious.Core.Baud;
using Baudacious.Core.Commands;

namespace Baudacious.Core.Tests;

public class CommandParserTests
{
    private static readonly CommandVocabulary Vocabulary = new()
    {
        Phosphors = ["color", "green", "amber", "white"],
        Profiles = ["pwsh", "cmd", "wsl"],
        Fonts = ["unscii", "terminus", "spleen"],
    };

    [Fact]
    public void AnEmptyLineIsNotAnError()
    {
        Assert.IsType<LocalCommand.Nothing>(CommandParser.Parse("   "));
    }

    [Fact]
    public void BaudTakesARate()
    {
        var command = Assert.IsType<LocalCommand.SetRate>(CommandParser.Parse("baud 2400"));
        Assert.Equal(2400, command.Rate.Baud);
    }

    [Fact]
    public void BaudTakesAFramingWithIt()
    {
        var command = Assert.IsType<LocalCommand.SetRate>(CommandParser.Parse("baud 1200 8N2"));
        Assert.Equal(1200, command.Rate.Baud);
        Assert.Equal(Framing.N82, command.Rate.Framing);
    }

    [Fact]
    public void BaudUnderstandsUnlimited()
    {
        var command = Assert.IsType<LocalCommand.SetRate>(CommandParser.Parse("baud unlimited"));
        Assert.True(command.Rate.IsUnlimited);
    }

    [Fact]
    public void ARateThatIsNotOneSaysSo()
    {
        var failure = Assert.IsType<LocalCommand.Failed>(CommandParser.Parse("baud quickly"));
        Assert.Contains("quickly", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnambiguousAbbreviationIsTheCommand()
    {
        Assert.IsType<LocalCommand.Quit>(CommandParser.Parse("q"));
        Assert.IsType<LocalCommand.SetRate>(CommandParser.Parse("b 300"));
        Assert.IsType<LocalCommand.ShowHelp>(CommandParser.Parse("hel"));
    }

    [Fact]
    public void AnAmbiguousAbbreviationListsWhatItCouldBe()
    {
        // framing, flush and fontsize all begin with f.
        var failure = Assert.IsType<LocalCommand.Failed>(CommandParser.Parse("f"));
        Assert.Contains("ambiguous", failure.Message, StringComparison.Ordinal);
        Assert.Contains("flush", failure.Message, StringComparison.Ordinal);
        Assert.Contains("framing", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownCommandPointsAtHelp()
    {
        var failure = Assert.IsType<LocalCommand.Failed>(CommandParser.Parse("teleport"));
        Assert.Contains("help", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FramingIsParsedOnItsOwn()
    {
        var command = Assert.IsType<LocalCommand.SetFraming>(CommandParser.Parse("framing 7e1"));
        Assert.Equal(Framing.E71, command.Framing);
    }

    [Fact]
    public void FlushTakesNothing()
    {
        Assert.IsType<LocalCommand.Flush>(CommandParser.Parse("flush"));
        Assert.IsType<LocalCommand.Failed>(CommandParser.Parse("flush now"));
    }

    [Fact]
    public void PhosphorIsMatchedAgainstWhatTheRendererHas()
    {
        var command = Assert.IsType<LocalCommand.SetPhosphor>(
            CommandParser.Parse("phosphor AMBER", Vocabulary));

        // Canonical casing comes back, so the caller can hand it straight to the enum.
        Assert.Equal("amber", command.Name);
    }

    [Fact]
    public void AnUnknownPhosphorNamesTheRealOnes()
    {
        var failure = Assert.IsType<LocalCommand.Failed>(
            CommandParser.Parse("phosphor puce", Vocabulary));

        Assert.Contains("green", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FontSizeIsBounded()
    {
        Assert.Equal(3, Assert.IsType<LocalCommand.SetFontSize>(CommandParser.Parse("fontsize 3")).Scale);

        // Clamping quietly would leave someone who asked for 20 believing they got it.
        var failure = Assert.IsType<LocalCommand.Failed>(CommandParser.Parse("fontsize 20"));
        Assert.Contains("20", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BareCrtMeansToggle()
    {
        Assert.Null(Assert.IsType<LocalCommand.SetCrt>(CommandParser.Parse("crt")).On);
        Assert.True(Assert.IsType<LocalCommand.SetCrt>(CommandParser.Parse("crt on")).On);
        Assert.False(Assert.IsType<LocalCommand.SetCrt>(CommandParser.Parse("crt off")).On);
        Assert.IsType<LocalCommand.Failed>(CommandParser.Parse("crt maybe"));
    }

    [Fact]
    public void CrtNamesAnEffectAndAnIntensity()
    {
        var command = Assert.IsType<LocalCommand.SetCrtLevel>(CommandParser.Parse("crt curvature 0.5"));

        Assert.Equal("curvature", command.Effect);
        Assert.Equal(0.5, command.Value);
    }

    [Theory]
    [InlineData("bloom")]
    [InlineData("scanline")]
    [InlineData("vignette")]
    [InlineData("curvature")]
    [InlineData("mask")]
    [InlineData("persistence")]
    public void EveryEffectIsReachable(string effect)
    {
        var command = Assert.IsType<LocalCommand.SetCrtLevel>(CommandParser.Parse($"crt {effect} 0.25"));

        Assert.Equal(effect, command.Effect);
        Assert.Equal(0.25, command.Value);
    }

    [Fact]
    public void EffectNamesAreCaseInsensitiveAndComeBackCanonical()
    {
        Assert.Equal(
            "persistence",
            Assert.IsType<LocalCommand.SetCrtLevel>(CommandParser.Parse("crt PERSISTENCE 1")).Effect);
    }

    [Fact]
    public void AnIntensityOutsideItsRangeIsRefusedRatherThanClamped()
    {
        var failure = Assert.IsType<LocalCommand.Failed>(CommandParser.Parse("crt curvature 3"));

        Assert.Contains("0-1", failure.Message, StringComparison.Ordinal);
        Assert.Contains("3", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BloomIsAllowedPastOneBecauseItAddsLight()
    {
        // The other five scale something that already exists and saturate at 1. Bloom is
        // additive, so 2.5 is a real setting — and the three paths that validate these
        // used to disagree about exactly this.
        Assert.Equal(
            2.5,
            Assert.IsType<LocalCommand.SetCrtLevel>(CommandParser.Parse("crt bloom 2.5")).Value);

        Assert.Contains(
            "0-4",
            Assert.IsType<LocalCommand.Failed>(CommandParser.Parse("crt bloom 9")).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheCeilingIsOneTableRatherThanThree()
    {
        Assert.Equal(CrtEffect.BloomCeiling, CrtEffect.CeilingOf("bloom"));
        Assert.Equal(CrtEffect.BloomCeiling, CrtEffect.CeilingOf("BLOOM"));

        foreach (var effect in CrtEffect.Names.Where(n => n != "bloom"))
        {
            Assert.Equal(1.0, CrtEffect.CeilingOf(effect));
        }
    }

    [Fact]
    public void EveryEffectNameIsRecognisedAndAnythingElseIsNot()
    {
        foreach (var effect in CrtEffect.Names)
        {
            Assert.Equal(effect, CrtEffect.Canonical(effect.ToUpperInvariant()));
        }

        Assert.Null(CrtEffect.Canonical("sparkle"));
        Assert.Null(CrtEffect.Canonical(null));
    }

    [Fact]
    public void AnEffectWithoutAValueSaysWhatItWants()
    {
        Assert.Contains(
            "crt mask <0-1>",
            Assert.IsType<LocalCommand.Failed>(CommandParser.Parse("crt mask")).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AnEffectValueThatIsNotANumberIsRefused()
    {
        Assert.Contains(
            "not a number",
            Assert.IsType<LocalCommand.Failed>(CommandParser.Parse("crt bloom lots")).Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CrtStillTogglesAndSwitches()
    {
        // Adding the effect form must not cost the form that was there first.
        Assert.Null(Assert.IsType<LocalCommand.SetCrt>(CommandParser.Parse("crt")).On);
        Assert.True(Assert.IsType<LocalCommand.SetCrt>(CommandParser.Parse("crt on")).On);
        Assert.False(Assert.IsType<LocalCommand.SetCrt>(CommandParser.Parse("crt off")).On);
        Assert.IsType<LocalCommand.Failed>(CommandParser.Parse("crt sideways"));
    }

    [Fact]
    public void FastEchoIsASwitchLikeCrt()
    {
        Assert.Null(Assert.IsType<LocalCommand.SetFastEcho>(CommandParser.Parse("fastecho")).On);
        Assert.True(Assert.IsType<LocalCommand.SetFastEcho>(CommandParser.Parse("fastecho on")).On);
        Assert.False(Assert.IsType<LocalCommand.SetFastEcho>(CommandParser.Parse("fastecho off")).On);

        var failure = Assert.IsType<LocalCommand.Failed>(CommandParser.Parse("fastecho sometimes"));
        Assert.Contains("sometimes", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheSecondLetterSettlesMostFVerbs()
    {
        // fastecho, flush and framing diverge at the second letter; font and fontsize do
        // not, so "fo" is genuinely ambiguous and says so.
        Assert.IsType<LocalCommand.SetFastEcho>(CommandParser.Parse("fa"));
        Assert.IsType<LocalCommand.Flush>(CommandParser.Parse("fl"));
        Assert.IsType<LocalCommand.SetFraming>(CommandParser.Parse("fr 8N1"));

        var failure = Assert.IsType<LocalCommand.Failed>(CommandParser.Parse("fo 3"));
        Assert.Contains("font", failure.Message, StringComparison.Ordinal);
        Assert.Contains("fontsize", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnExactNameBeatsBeingAPrefixOfAnother()
    {
        // "font" is a prefix of "fontsize", so without exact-match-first it would be
        // ambiguous with itself.
        Assert.IsType<LocalCommand.SetFont>(CommandParser.Parse("font terminus", Vocabulary));
        Assert.IsType<LocalCommand.SetFontSize>(CommandParser.Parse("fontsize 3"));
    }

    [Fact]
    public void AnUnknownFontNamesTheRealOnes()
    {
        var failure = Assert.IsType<LocalCommand.Failed>(
            CommandParser.Parse("font comic", Vocabulary));

        Assert.Contains("unscii", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionNewTakesAProfile()
    {
        var command = Assert.IsType<LocalCommand.NewSession>(
            CommandParser.Parse("session new cmd", Vocabulary));

        Assert.Equal("cmd", command.Profile);
    }

    [Fact]
    public void SessionNewKeepsTheDistributionSuffix()
    {
        var command = Assert.IsType<LocalCommand.NewSession>(
            CommandParser.Parse("session new wsl:Ubuntu", Vocabulary));

        Assert.Equal("wsl:Ubuntu", command.Profile);
    }

    [Fact]
    public void AnUnknownProfileIsRefused()
    {
        var failure = Assert.IsType<LocalCommand.Failed>(
            CommandParser.Parse("session new zsh", Vocabulary));

        Assert.Contains("pwsh", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionWithoutNewExplainsItself()
    {
        Assert.IsType<LocalCommand.Failed>(CommandParser.Parse("session", Vocabulary));
        Assert.IsType<LocalCommand.Failed>(CommandParser.Parse("session list", Vocabulary));
    }

    [Fact]
    public void EveryVerbHasExactlyOneLineOfHelp()
    {
        Assert.Equal(CommandVocabulary.Verbs.Length, CommandVocabulary.Usage.Length);

        for (var i = 0; i < CommandVocabulary.Verbs.Length; i++)
        {
            Assert.StartsWith(CommandVocabulary.Verbs[i], CommandVocabulary.Usage[i], StringComparison.Ordinal);
        }
    }
}
