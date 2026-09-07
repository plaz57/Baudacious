using Baudacious.Core.Config;

namespace Baudacious.Core.Tests;

public class IniDocumentTests
{
    private const string Sample =
        """
        ; Baudacious settings.
        ; Edit and save; changes apply within a second.

        [line]
        baud = 1200        ; 110 to 115200, or 'unlimited'
        framing = 8N1

        [display]
        phosphor = green
          font-scale = 2
        """;

    [Theory]
    [InlineData("")]
    [InlineData("\n")]
    [InlineData("[a]\nk = v")]
    [InlineData("[a]\nk = v\n")]
    [InlineData("[a]\r\nk = v\r\n")]
    [InlineData("; just a comment")]
    [InlineData("\n\n\n")]
    [InlineData("  [spaced]  \n\tk\t=\tv\t; note\n")]
    public void ParsingAndWritingBackChangesNothing(string text)
    {
        // The property everything else depends on: the file is the user's, and reading it
        // must not be a reason to rewrite it.
        Assert.Equal(text, IniDocument.Parse(text).ToText());
    }

    [Fact]
    public void TheSampleFileRoundTrips()
    {
        Assert.Equal(Sample, IniDocument.Parse(Sample).ToText());
    }

    [Fact]
    public void ValuesAreFoundBySectionAndKey()
    {
        var ini = IniDocument.Parse(Sample);

        Assert.Equal("1200", ini.Get("line", "baud"));
        Assert.Equal("8N1", ini.Get("line", "framing"));
        Assert.Equal("green", ini.Get("display", "phosphor"));
        Assert.Equal("2", ini.Get("display", "font-scale"));
    }

    [Fact]
    public void SectionsAndKeysAreCaseInsensitive()
    {
        var ini = IniDocument.Parse(Sample);

        Assert.Equal("1200", ini.Get("LINE", "BAUD"));
        Assert.Equal("green", ini.Get("Display", "Phosphor"));
    }

    [Fact]
    public void AMissingKeyIsNull()
    {
        var ini = IniDocument.Parse(Sample);

        Assert.Null(ini.Get("line", "parity"));
        Assert.Null(ini.Get("nonesuch", "baud"));
    }

    [Fact]
    public void ACommentAfterAValueIsNotPartOfIt()
    {
        // The value is 1200, not "1200        ; 110 to 115200, or 'unlimited'".
        Assert.Equal("1200", IniDocument.Parse(Sample).Get("line", "baud"));
    }

    [Fact]
    public void ASemicolonInsideAValueStaysInIt()
    {
        // Only whitespace then ';' starts a comment, so a command line survives.
        var ini = IniDocument.Parse("[profile.odd]\ncommand = sh -c a;b\n");

        Assert.Equal("sh -c a;b", ini.Get("profile.odd", "command"));
    }

    [Fact]
    public void AssignmentKeepsEverythingAroundTheValue()
    {
        var ini = IniDocument.Parse(Sample);
        ini.Set("line", "baud", "2400");

        var text = ini.ToText();

        // Same indentation, same spacing, same trailing comment; only the number moved.
        Assert.Contains("baud = 2400        ; 110 to 115200, or 'unlimited'", text, StringComparison.Ordinal);
        Assert.Contains("; Baudacious settings.", text, StringComparison.Ordinal);
        Assert.Contains("  font-scale = 2", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AssignmentPreservesOddSpacing()
    {
        var ini = IniDocument.Parse("[a]\n\tk\t=\tv\t; note");
        ini.Set("a", "k", "w");

        Assert.Equal("[a]\n\tk\t=\tw\t; note", ini.ToText());
    }

    [Fact]
    public void ANewKeyLandsInsideItsSection()
    {
        var ini = IniDocument.Parse(Sample);
        ini.Set("line", "parity", "even");

        var lines = ini.ToText().ReplaceLineEndings("\n").Split('\n');
        var section = Array.IndexOf(lines, "[line]");
        var added = Array.IndexOf(lines, "parity = even");

        Assert.True(added > section, "the new key should follow its section header");
        Assert.True(added < Array.IndexOf(lines, "[display]"), "and precede the next section");
    }

    [Fact]
    public void ANewSectionIsAppendedWithABlankLineBeforeIt()
    {
        var ini = IniDocument.Parse("[a]\nk = v\n");
        ini.Set("b", "j", "w");

        Assert.Equal("[a]\nk = v\n\n[b]\nj = w\n", ini.ToText());
    }

    [Fact]
    public void SettingIntoAnEmptyDocumentBuildsIt()
    {
        var ini = IniDocument.Parse(string.Empty);
        ini.Set("line", "baud", "300");

        Assert.Equal("[line]", ini.ToText().ReplaceLineEndings("\n").Split('\n')[0]);
        Assert.Equal("300", ini.Get("line", "baud"));
    }

    [Fact]
    public void SectionsAndKeysCanBeEnumerated()
    {
        var ini = IniDocument.Parse(Sample);

        Assert.Equal(["line", "display"], ini.Sections());
        Assert.Equal(["baud", "framing"], ini.Keys("line"));
    }

    [Fact]
    public void ALineNumberIsAvailableForPointingAtABadValue()
    {
        var ini = IniDocument.Parse(Sample);

        // Two comments, a blank, then [line], then baud.
        Assert.Equal(5, ini.LineOf("line", "baud"));
        Assert.Equal(0, ini.LineOf("line", "nonesuch"));
    }

    [Fact]
    public void AMalformedLineIsReportedWithItsNumber()
    {
        var ini = IniDocument.Parse("[a]\nthis is not a setting\nk = v\n");

        var problem = Assert.Single(ini.Problems);
        Assert.Equal(2, problem.Line);
        Assert.Contains("key = value", problem.Message, StringComparison.Ordinal);

        // And the rest of the file is still read.
        Assert.Equal("v", ini.Get("a", "k"));
    }

    [Fact]
    public void AnUnclosedSectionHeaderIsReported()
    {
        var ini = IniDocument.Parse("[oops\nk = v\n");

        var problem = Assert.Single(ini.Problems);
        Assert.Equal(1, problem.Line);
        Assert.Contains("[name]", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ANamelessSettingIsReported()
    {
        var ini = IniDocument.Parse("[a]\n = v\n");

        Assert.Contains("needs a name", Assert.Single(ini.Problems).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ADuplicateKeyIsReportedAndTheLastOneWins()
    {
        var ini = IniDocument.Parse("[a]\nk = first\nk = second\n");

        Assert.Equal("second", ini.Get("a", "k"));

        var problem = Assert.Single(ini.Problems);
        Assert.Equal(3, problem.Line);
        Assert.Contains("more than once", problem.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMalformedFileIsNeverRewritten()
    {
        const string broken = "[a]\nthis is not a setting\n";

        Assert.Equal(broken, IniDocument.Parse(broken).ToText());
    }

    [Fact]
    public void KeysBeforeAnySectionBelongToTheRoot()
    {
        var ini = IniDocument.Parse("stray = 1\n[a]\nk = v\n");

        Assert.Equal("1", ini.Get(string.Empty, "stray"));
        Assert.Empty(ini.Problems);
    }

    [Fact]
    public void RemovingAKeyTakesItsWholeLine()
    {
        var ini = IniDocument.Parse("[a]\nj = 1\nk = 2  ; a note\nl = 3\n");

        Assert.True(ini.Remove("a", "k"));
        Assert.Null(ini.Get("a", "k"));

        // The comment belonged to the setting, so it went with it.
        Assert.Equal("[a]\nj = 1\nl = 3\n", ini.ToText());
    }

    [Fact]
    public void RemovingWhatIsNotThereChangesNothing()
    {
        var ini = IniDocument.Parse("[a]\nk = v\n");

        Assert.False(ini.Remove("a", "nonesuch"));
        Assert.False(ini.Remove("nonesuch", "k"));
        Assert.Equal("[a]\nk = v\n", ini.ToText());
    }

    [Fact]
    public void RemovingTheLastKeyLeavesTheSectionHeader()
    {
        // It may carry the user's own commentary, and an empty section reads as a
        // place to put things rather than as debris.
        var ini = IniDocument.Parse("[a]\nk = v\n");
        ini.Remove("a", "k");

        Assert.Equal("[a]\n", ini.ToText());
        Assert.Equal(["a"], ini.Sections());
    }

    [Fact]
    public void TheFilesOwnLineEndingIsKept()
    {
        var ini = IniDocument.Parse("[a]\r\nk = v\r\n");
        ini.Set("a", "k", "w");

        Assert.Equal("[a]\r\nk = w\r\n", ini.ToText());
    }
}
