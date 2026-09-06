using Baudacious.Core.Commands;

namespace Baudacious.Core.Tests;

public class CommandLineTests
{
    private static readonly CommandVocabulary Vocabulary = new()
    {
        Phosphors = ["color", "green", "amber", "white"],
        Profiles = ["pwsh", "cmd", "wsl"],
    };

    private static CommandLine Typed(string text)
    {
        var line = new CommandLine();
        line.Insert(text);
        return line;
    }

    [Fact]
    public void TypingBuildsTheLine()
    {
        var line = Typed("baud");
        Assert.Equal("baud", line.Text);
        Assert.Equal(4, line.Caret);
    }

    [Fact]
    public void EditingHappensAtTheCaret()
    {
        var line = Typed("bud");
        line.MoveHome();
        line.MoveRight();
        line.Insert('a');

        Assert.Equal("baud", line.Text);
        Assert.Equal(2, line.Caret);
    }

    [Fact]
    public void BackspaceAtTheStartDoesNothing()
    {
        var line = new CommandLine();
        Assert.False(line.Backspace());
        Assert.Equal(string.Empty, line.Text);
    }

    [Fact]
    public void DeleteWordBackTakesTheSpaceWithIt()
    {
        var line = Typed("baud 2400");
        line.DeleteWordBack();

        // The separating space stays, as readline leaves it, so another rate can be typed
        // straight away.
        Assert.Equal("baud ", line.Text);
        Assert.Equal(5, line.Caret);
    }

    [Fact]
    public void DeleteToStartLeavesWhatIsRightOfTheCaret()
    {
        var line = Typed("baud 2400");
        line.MoveHome();
        line.MoveRight();
        line.MoveRight();
        line.MoveRight();
        line.MoveRight();
        line.MoveRight();
        line.DeleteToStart();

        Assert.Equal("2400", line.Text);
        Assert.Equal(0, line.Caret);
    }

    [Fact]
    public void SubmittingRemembersAndClears()
    {
        var line = Typed("baud 300");
        Assert.Equal("baud 300", line.Submit());

        Assert.Equal(string.Empty, line.Text);
        Assert.Equal(["baud 300"], line.History);
    }

    [Fact]
    public void BlanksAndImmediateRepeatsAreNotRemembered()
    {
        var line = new CommandLine();

        line.Insert("   ");
        line.Submit();
        line.Insert("flush");
        line.Submit();
        line.Insert("flush");
        line.Submit();

        Assert.Equal(["flush"], line.History);
    }

    [Fact]
    public void HistoryWalksBackAndReturnsToWhatWasBeingTyped()
    {
        var line = new CommandLine();
        line.Insert("baud 300");
        line.Submit();
        line.Insert("flush");
        line.Submit();

        line.Insert("half-typ");
        Assert.True(line.HistoryPrevious());
        Assert.Equal("flush", line.Text);
        Assert.True(line.HistoryPrevious());
        Assert.Equal("baud 300", line.Text);

        // Nowhere further back to go.
        Assert.False(line.HistoryPrevious());

        Assert.True(line.HistoryNext());
        Assert.Equal("flush", line.Text);
        Assert.True(line.HistoryNext());
        Assert.Equal("half-typ", line.Text);
        Assert.False(line.HistoryNext());
    }

    [Fact]
    public void HistoryIsBounded()
    {
        var line = new CommandLine();
        for (var i = 0; i < CommandLine.HistoryLimit + 20; i++)
        {
            line.Insert($"fontsize {i}");
            line.Submit();
        }

        Assert.Equal(CommandLine.HistoryLimit, line.History.Count);
        Assert.Equal("fontsize 20", line.History[0]);
    }
}

public class CommandCompleterTests
{
    private static readonly CommandVocabulary Vocabulary = new()
    {
        Phosphors = ["color", "green", "amber", "white"],
        Profiles = ["pwsh", "cmd", "wsl"],
    };

    private static Completion Complete(string line) =>
        CommandCompleter.Complete(line, line.Length, Vocabulary);

    [Fact]
    public void AUniqueVerbIsTakenWithASpace()
    {
        var completion = Complete("ba");

        Assert.Equal("baud ", completion.Line);
        Assert.Equal(5, completion.Caret);
        Assert.Empty(completion.Candidates);
    }

    [Fact]
    public void SeveralMatchesExtendToWhatTheyAgreeOn()
    {
        // framing, flush and fontsize share only "f", so nothing is added and all three
        // are offered.
        var completion = Complete("f");

        Assert.Equal("f", completion.Line);
        Assert.Equal(3, completion.Candidates.Count);
    }

    [Fact]
    public void ACommonPrefixIsTakenEvenWhenItIsNotTheWholeWord()
    {
        // "fl" and "fo" diverge, but "fr" reaches "framing" alone.
        Assert.Equal("framing ", Complete("fr").Line);
    }

    [Fact]
    public void ValuesAreCompletedInTheirOwnPosition()
    {
        Assert.Equal("phosphor amber ", Complete("phosphor am").Line);
        Assert.Equal("crt off ", Complete("crt of").Line);
        Assert.Equal("framing 7E1 ", Complete("framing 7").Line);
    }

    [Fact]
    public void RatesComeFromTheStandardList()
    {
        Assert.Equal("baud unlimited ", Complete("baud u").Line);
        Assert.Equal("baud 9600 ", Complete("baud 9").Line);
    }

    [Fact]
    public void TheThirdWordOfSessionIsAProfile()
    {
        Assert.Equal("session new ", Complete("session n").Line);
        Assert.Equal("session new pwsh ", Complete("session new p").Line);
    }

    [Fact]
    public void AnUnknownVerbOffersNothingForItsArgument()
    {
        var completion = Complete("teleport ho");

        Assert.Equal("teleport ho", completion.Line);
        Assert.Empty(completion.Candidates);
    }

    [Fact]
    public void CompletionWorksInTheMiddleOfALine()
    {
        // Caret after "ph", with " green" already typed beyond it.
        var completion = CommandCompleter.Complete("ph green", 2, Vocabulary);

        Assert.Equal("phosphor  green", completion.Line);
        Assert.Equal(9, completion.Caret);
    }

    [Fact]
    public void TheLineIsUnchangedWhenNothingMatches()
    {
        var completion = Complete("phosphor zz");

        Assert.Equal("phosphor zz", completion.Line);
        Assert.Empty(completion.Candidates);
    }

    [Fact]
    public void CompletingThroughTheLineEditorMovesTheCaret()
    {
        var line = new CommandLine();
        line.Insert("qu");
        line.Complete(Vocabulary);

        Assert.Equal("quit ", line.Text);
        Assert.Equal(5, line.Caret);
    }
}
