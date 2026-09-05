using Baudacious.App;

// Phase 1: the only thing Baudacious can do yet is meter a recorded stream. Phase 3 makes
// the default action "open a window and host a shell", and keeps `meter` as a diagnostic.
return args.Length == 0 ? Help() : args[0].ToLowerInvariant() switch
{
    "meter" => MeterCommand.Run(args[1..]),
    "shell" => ShellCommand.Run(args[1..]),
    _ => Help(),
};

static int Help()
{
    Console.Error.WriteLine("""
        Baudacious — a terminal with a baud rate.

          shell [options]                 host a live shell at a chosen baud rate
          meter <capture.bin> [options]    play a recorded byte stream at a chosen rate

        Both paint into the host console for now. The GPU window and the CRT
        shader arrive in Phases 3 and 5; see openspec/changes/baudacious-v1/tasks.md.
        """);
    return 1;
}
