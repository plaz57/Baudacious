using Baudacious.App.Config;
using Baudacious.Core.Baud;
using Baudacious.Render;

namespace Baudacious.Core.Tests;

/// <summary>
/// The two-file arrangement, exercised against a real directory.
/// </summary>
/// <remarks>
/// Worth touching the disk for: the whole point of separating the user's file from the
/// persisted state is what does and does not get written, and that cannot be checked
/// against an abstraction over the file system without also trusting the abstraction.
/// </remarks>
public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "baudacious-config-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test over.
        }
    }

    private string ConfigPath => Path.Combine(_directory, "baudacious.ini");

    private string StatePath => Path.Combine(_directory, "state.ini");

    [Fact]
    public void FirstRunWritesACommentedFileAndStartsWithItsValues()
    {
        var store = ConfigStore.Load(_directory);

        Assert.True(store.CreatedConfig);
        Assert.True(File.Exists(ConfigPath));
        Assert.Empty(store.Problems);
        Assert.Equal(Settings.Defaults, store.Settings);

        var text = File.ReadAllText(ConfigPath);
        Assert.StartsWith("; Baudacious settings.", text, StringComparison.Ordinal);
        Assert.Contains("[keys]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ASecondRunDoesNotRewriteTheFile()
    {
        ConfigStore.Load(_directory);
        var first = File.ReadAllBytes(ConfigPath);

        var store = ConfigStore.Load(_directory);

        Assert.False(store.CreatedConfig);
        Assert.Equal(first, File.ReadAllBytes(ConfigPath));
    }

    [Fact]
    public void TheConfigFileIsHonouredOnAFreshInstall()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(ConfigPath, "[line]\nbaud = 300\n[display]\nphosphor = amber\n");

        var store = ConfigStore.Load(_directory);

        Assert.Equal(300, store.Settings.Rate.Baud);
        Assert.Equal(Phosphor.Amber, store.Settings.Display.Phosphor);
    }

    [Fact]
    public void StateWinsOverTheConfigFile()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(ConfigPath, "[line]\nbaud = 300\n[display]\nphosphor = amber\n");
        File.WriteAllText(StatePath, "[line]\nbaud = 9600\n");

        var store = ConfigStore.Load(_directory);

        Assert.Equal(9600, store.Settings.Rate.Baud);
        Assert.Equal(Phosphor.Amber, store.Settings.Display.Phosphor);
    }

    [Fact]
    public void SavingStateLeavesTheConfigFileByteForByteUnchanged()
    {
        var store = ConfigStore.Load(_directory);
        var before = File.ReadAllBytes(ConfigPath);

        store.SaveState(store.Settings with
        {
            Rate = LineRate.Of(9600),
            Display = store.Settings.Display with { Phosphor = Phosphor.Amber },
        });

        Assert.Equal(before, File.ReadAllBytes(ConfigPath));
        Assert.True(File.Exists(StatePath));
    }

    [Fact]
    public void WhatWasSavedComesBack()
    {
        var store = ConfigStore.Load(_directory);

        store.SaveState(store.Settings with
        {
            Rate = LineRate.Of(9600, Framing.E71),
            Display = store.Settings.Display with
            {
                Phosphor = Phosphor.Amber,
                FontScale = 3,
                Crt = store.Settings.Display.Crt with { Enabled = false, Bloom = 0.25 },
            },
            Window = new WindowGeometry { Columns = 120, Rows = 40, Fullscreen = true },
        });

        var next = ConfigStore.Load(_directory).Settings;

        Assert.Equal(9600, next.Rate.Baud);
        Assert.Equal(Framing.E71, next.Rate.Framing);
        Assert.Equal(Phosphor.Amber, next.Display.Phosphor);
        Assert.Equal(3, next.Display.FontScale);
        Assert.False(next.Display.Crt.Enabled);
        Assert.Equal(0.25, next.Display.Crt.Bloom);
        Assert.Equal(120, next.Window.Columns);
        Assert.Equal(40, next.Window.Rows);
        Assert.True(next.Window.Fullscreen);
    }

    [Fact]
    public void SavingStateTwiceRewritesRatherThanAccumulates()
    {
        var store = ConfigStore.Load(_directory);

        store.SaveState(store.Settings with { Rate = LineRate.Of(300) });
        store.SaveState(store.Settings with { Rate = LineRate.Of(2400) });

        Assert.Equal(2400, ConfigStore.Load(_directory).Settings.Rate.Baud);

        var lines = File.ReadAllLines(StatePath);
        Assert.Single(lines, l => l.StartsWith("baud", StringComparison.Ordinal));
    }

    [Fact]
    public void AMalformedConfigFileIsReportedAndNeverRewritten()
    {
        Directory.CreateDirectory(_directory);
        const string broken = "[line]\nthis is not a setting\nbaud = 2400\n";
        File.WriteAllText(ConfigPath, broken);

        var store = ConfigStore.Load(_directory);

        // Started anyway, honoured the good line, complained about the bad one.
        Assert.Equal(2400, store.Settings.Rate.Baud);
        Assert.Contains("line 2", Assert.Single(store.Problems), StringComparison.Ordinal);
        Assert.Equal(broken, File.ReadAllText(ConfigPath));
    }

    [Fact]
    public void AnEditWinsOverTheStateForTheSettingItTouched()
    {
        // The interesting case. State beats the file, so a saved palette would otherwise
        // make editing the palette in the file do nothing at all — and hot reload is
        // specified to change the display within a second of a save.
        var store = ConfigStore.Load(_directory);
        store.SaveState(store.Settings with
        {
            Rate = LineRate.Of(9600),
            Display = store.Settings.Display with { Phosphor = Phosphor.Green },
        });

        File.WriteAllText(ConfigPath, "[display]\nphosphor = white\n");

        var problems = new List<string>();
        var reloaded = store.Reread(problems);

        Assert.NotNull(reloaded);
        Assert.Empty(problems);

        // The palette was edited, so the file owns it now.
        Assert.Equal(Phosphor.White, reloaded.Display.Phosphor);

        // The rate was not touched, so the state still holds it.
        Assert.Equal(9600, reloaded.Rate.Baud);
    }

    [Fact]
    public void AnEditKeepsWinningForTheRestOfTheRun()
    {
        var store = ConfigStore.Load(_directory);
        store.SaveState(store.Settings with
        {
            Display = store.Settings.Display with { Phosphor = Phosphor.Green },
        });

        File.WriteAllText(ConfigPath, "[display]\nphosphor = white\n");
        Assert.Equal(Phosphor.White, store.Reread([])!.Display.Phosphor);

        // A later, unrelated save must not let the stale state take the palette back.
        File.WriteAllText(
            ConfigPath, "[display]\nphosphor = white\nfont-scale = 3\n");

        var reloaded = store.Reread([]);
        Assert.Equal(Phosphor.White, reloaded!.Display.Phosphor);
        Assert.Equal(3, reloaded.Display.FontScale);
    }

    [Fact]
    public void AFirstReadIsNotMistakenForAnEdit()
    {
        // Every setting in the file differs from "nothing read yet"; counting that as an
        // edit would take the state out of play before it had been applied once.
        Directory.CreateDirectory(_directory);
        File.WriteAllText(ConfigPath, "[line]\nbaud = 300\n");
        File.WriteAllText(StatePath, "[line]\nbaud = 9600\n");

        Assert.Equal(9600, ConfigStore.Load(_directory).Settings.Rate.Baud);
    }

    [Fact]
    public void RereadingWithNoEditKeepsTheState()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(ConfigPath, "[line]\nbaud = 300\n");
        File.WriteAllText(StatePath, "[line]\nbaud = 9600\n");

        var store = ConfigStore.Load(_directory);

        Assert.Equal(9600, store.Reread([])!.Rate.Baud);
    }

    [Fact]
    public void RereadingAMissingFileGivesNothingRatherThanDefaults()
    {
        // An editor's atomic replace can leave the file absent for an instant. Falling back
        // to defaults on a timing accident would be worse than waiting for the next event.
        var store = ConfigStore.Load(_directory);
        File.Delete(ConfigPath);

        Assert.Null(store.Reread([]));
    }

    [Fact]
    public void TheStoreNamesItsOwnFiles()
    {
        var store = ConfigStore.Load(_directory);

        Assert.Equal(ConfigPath, store.ConfigPath);
        Assert.Equal(StatePath, store.StatePath);
        Assert.Equal(_directory, store.Directory);
    }
}
