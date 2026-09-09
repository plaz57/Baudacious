using Baudacious.App;
using Baudacious.App.Config;

namespace Baudacious.Core.Tests;

/// <summary>
/// Which shells exist and how each is launched.
/// </summary>
/// <remarks>
/// These exist because <c>[profile.name]</c> sections were parsed, validated and then
/// ignored: any name that was not cmd or wsl fell through to PowerShell, so a configured
/// profile silently started the wrong shell and a typo did too.
/// </remarks>
public class ShellProfileTests
{
    private static ShellProfiles With(params ShellProfileConfig[] configured) => new(configured);

    private static ShellProfileConfig GitBash =>
        new("gitbash", @"C:\Program Files\Git\bin\bash.exe", "--login -i", null);

    [Fact]
    public void TheThreeBuiltInsNeedNoConfiguration()
    {
        Assert.Equal(["pwsh", "cmd", "wsl"], ShellProfiles.Builtin.Names);
    }

    [Fact]
    public void AConfiguredProfileIsLaunchable()
    {
        var (start, setup) = With(GitBash).Build("gitbash", fastEcho: false);

        Assert.Equal(@"C:\Program Files\Git\bin\bash.exe", start.Executable);
        Assert.Equal("--login -i", start.Arguments);
        Assert.Empty(setup);
    }

    [Fact]
    public void AConfiguredProfileIsNamedAmongTheRest()
    {
        Assert.Equal(["pwsh", "cmd", "wsl", "gitbash"], With(GitBash).Names);
    }

    [Fact]
    public void AConfiguredNameIsMatchedWhateverItsCase()
    {
        var (start, _) = With(GitBash).Build("GitBash", fastEcho: false);

        Assert.Equal(@"C:\Program Files\Git\bin\bash.exe", start.Executable);
    }

    [Fact]
    public void AnUnknownProfileThrowsRatherThanStartingPowerShell()
    {
        // The bug this file exists for. Silently launching the wrong shell is worse than
        // a message, so the message names what does exist.
        var ex = Assert.Throws<InvalidOperationException>(
            () => With(GitBash).Build("nonesuch", fastEcho: false));

        Assert.Contains("nonesuch", ex.Message, StringComparison.Ordinal);
        Assert.Contains("gitbash", ex.Message, StringComparison.Ordinal);
        Assert.Contains("pwsh", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ABuiltInCanBePointedSomewhereElse()
    {
        // Overriding pwsh is how someone runs a different PowerShell, and the name should
        // not appear twice for it.
        var profiles = With(new ShellProfileConfig("pwsh", @"D:\pwsh-preview\pwsh.exe", "-NoLogo", "pwsh"));
        var (start, _) = profiles.Build("pwsh", fastEcho: true);

        Assert.Equal(@"D:\pwsh-preview\pwsh.exe", start.Executable);
        Assert.Equal(["pwsh", "cmd", "wsl"], profiles.Names);
    }

    [Fact]
    public void LikePwshBorrowsTheTamingAfterTheUsersOwnArguments()
    {
        var profiles = With(new ShellProfileConfig("psx", "pwsh.exe", "-NoLogo -NoProfile", "pwsh"));

        var fast = profiles.Build("psx", fastEcho: true).Start.Arguments;
        Assert.StartsWith("-NoLogo -NoProfile ", fast, StringComparison.Ordinal);
        Assert.Contains("Remove-Module PSReadLine", fast, StringComparison.Ordinal);

        var rich = profiles.Build("psx", fastEcho: false).Start.Arguments;
        Assert.Contains("Set-PSReadLineOption -PredictionSource None", rich, StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutLikeAProfileGetsNoTaming()
    {
        var arguments = With(GitBash).Build("gitbash", fastEcho: true).Start.Arguments;

        Assert.Equal("--login -i", arguments);
        Assert.DoesNotContain("PSReadLine", arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyPowerShellHasAnEditorWorthReplacing()
    {
        var profiles = With(
            GitBash,
            new ShellProfileConfig("psx", "pwsh.exe", string.Empty, "pwsh"));

        Assert.True(profiles.SupportsFastEcho("pwsh"));
        Assert.True(profiles.SupportsFastEcho("psx"));
        Assert.False(profiles.SupportsFastEcho("gitbash"));
        Assert.False(profiles.SupportsFastEcho("cmd"));
        Assert.False(profiles.SupportsFastEcho("wsl"));
    }

    [Fact]
    public void OnlyTheProfilesWithAnEditorHaveAFloorToWarnAbout()
    {
        var profiles = With(GitBash);

        Assert.Equal(9600, profiles.UsableFloorBaud("pwsh", fastEcho: false));
        Assert.Equal(0, profiles.UsableFloorBaud("pwsh", fastEcho: true));
        Assert.Equal(0, profiles.UsableFloorBaud("gitbash", fastEcho: false));
        Assert.Equal(0, profiles.UsableFloorBaud("cmd", fastEcho: false));
    }

    [Fact]
    public void WslStillTakesADistributionAfterAColon()
    {
        var (start, _) = With(GitBash).Build("wsl:Ubuntu", fastEcho: false);

        Assert.EndsWith("wsl.exe", start.Executable, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ubuntu", start.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void PowerShellIsTamedThroughItsArgumentsRatherThanByTyping()
    {
        // Typed setup is echoed by the shell, so a new window used to open with two lines
        // of Baudacious configuring itself at the top.
        var (start, setup) = ShellProfiles.Builtin.Build("pwsh", fastEcho: false);

        Assert.Empty(setup);
        Assert.Contains("-NoExit", start.Arguments, StringComparison.Ordinal);
        Assert.Contains("Set-PSReadLineOption", start.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingReplacesThePrompt()
    {
        // It cost the working directory to save 0.9 bytes a keystroke.
        foreach (var fastEcho in new[] { true, false })
        {
            var (start, setup) = ShellProfiles.Builtin.Build("pwsh", fastEcho);

            Assert.DoesNotContain("function prompt", start.Arguments, StringComparison.Ordinal);
            Assert.DoesNotContain(setup, c => c.Contains("function prompt", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void TheMessageNamesTheProfileAndWhereToAddOne()
    {
        // It is the only thing the user gets: an unknown profile now launches nothing, so
        // the message has to be enough to act on.
        var ex = Assert.Throws<InvalidOperationException>(
            () => ShellProfiles.Builtin.Build("gitbash", fastEcho: false));

        Assert.Contains("baudacious.ini", ex.Message, StringComparison.Ordinal);
        Assert.Contains("[profile.", ex.Message, StringComparison.Ordinal);
    }
}
