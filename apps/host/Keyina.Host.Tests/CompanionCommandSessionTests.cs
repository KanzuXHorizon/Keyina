using System.Diagnostics;
using Keyina.Host.Core.Hotkeys;
using Keyina.Host.Runtime;

namespace Keyina.Host.Tests;

internal static class CompanionCommandSessionTests
{
    [KeyinaTest("companion command arguments round trip every supported command")]
    private static void ArgumentsRoundTrip()
    {
        foreach (var command in Enum.GetValues<CompanionCommand>())
        {
            var argument = CompanionCommandProtocol.ToArgument(command);
            AssertEx.True(
                CompanionCommandProtocol.TryParseArgument(argument, out var parsed),
                $"Companion argument was rejected: {argument}.");
            AssertEx.Equal(command, parsed);
            AssertEx.True(
                CompanionCommandProtocol.EventName(command).StartsWith(
                    "Local\\Keyina.Command.",
                    StringComparison.Ordinal),
                "Companion event name escaped the current user session namespace.");
        }
    }

    [KeyinaTest("companion command parser rejects malformed or unknown arguments")]
    private static void RejectsUnknownArguments()
    {
        AssertEx.False(
            CompanionCommandProtocol.TryParseArgument(null, out _),
            "Null companion argument was accepted.");
        AssertEx.False(
            CompanionCommandProtocol.TryParseArgument("--show-settings", out _),
            "Unrelated argument was accepted.");
        AssertEx.False(
            CompanionCommandProtocol.TryParseArgument(
                "--companion-command=unknown",
                out _),
            "Unknown companion command was accepted.");
    }

    [KeyinaTest("companion commands map to the intended host commands")]
    private static void CommandsMapExactly()
    {
        AssertEx.Equal(
            "--companion-command=set-vietnamese-enabled",
            CompanionCommandProtocol.ToArgument(
                CompanionCommand.SetVietnameseEnabled));
        AssertEx.Equal(
            "--companion-command=set-vietnamese-disabled",
            CompanionCommandProtocol.ToArgument(
                CompanionCommand.SetVietnameseDisabled));
        AssertEx.Equal(
            HotkeyCommand.PushToTalkPressed,
            CompanionCommandProtocol.ToHotkeyCommand(
                CompanionCommand.PushToTalkPressed));
        AssertEx.Equal(
            HotkeyCommand.PushToTalkReleased,
            CompanionCommandProtocol.ToHotkeyCommand(
                CompanionCommand.PushToTalkReleased));
        AssertEx.Equal(
            HotkeyCommand.ToggleDictation,
            CompanionCommandProtocol.ToHotkeyCommand(
                CompanionCommand.ToggleDictation));
        AssertEx.Equal(
            HotkeyCommand.TranslateSelection,
            CompanionCommandProtocol.ToHotkeyCommand(
                CompanionCommand.TranslateSelection));
        AssertEx.Equal(
            HotkeyCommand.UndoTranslation,
            CompanionCommandProtocol.ToHotkeyCommand(
                CompanionCommand.UndoTranslation));
        AssertEx.Equal(
            HotkeyCommand.CancelDictation,
            CompanionCommandProtocol.ToHotkeyCommand(
                CompanionCommand.CancelActiveCommand));
    }

    [KeyinaTest("command companion starts without a WinForms bootstrap crash")]
    [KeyinaInteractiveTest]
    private static void StartsWithoutWinFormsBootstrapCrash()
    {
        var executable = Path.Combine(AppContext.BaseDirectory, "Keyina.Host.exe");
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = executable,
            Arguments = CompanionCommandProtocol.ToArgument(
                CompanionCommand.CancelActiveCommand),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        });
        AssertEx.NotNull(process, "Command companion process did not start.");

        var exited = process!.WaitForExit(milliseconds: 10_000);
        if (!exited)
        {
            process.Kill(entireProcessTree: true);
        }
        AssertEx.True(exited, "Command companion did not exit after an idle command.");

        var standardError = process.StandardError.ReadToEnd();
        AssertEx.Equal(
            0,
            process.ExitCode,
            $"Command companion crashed during WinForms startup: {standardError}");
    }

    [KeyinaTest("command companion exits only when no transient work remains")]
    private static void ExitPolicyRetainsOnlyActiveWork()
    {
        AssertEx.True(
            CompanionCommandSession.ShouldExit(
                commandInFlight: false,
                dictationActive: false,
                canUndoTranslation: false,
                translationPreviewCreated: false,
                interactiveWindowCreated: false),
            "Idle command companion remained resident.");
        AssertEx.False(
            CompanionCommandSession.ShouldExit(true, false, false, false, false),
            "Companion exited while a command was still in flight.");
        AssertEx.False(
            CompanionCommandSession.ShouldExit(false, true, false, false, false),
            "Active dictation companion exited.");
        AssertEx.False(
            CompanionCommandSession.ShouldExit(false, false, true, false, false),
            "Translation undo companion exited before expiry.");
        AssertEx.False(
            CompanionCommandSession.ShouldExit(false, false, false, true, false),
            "Translation preview companion exited while visible.");
        AssertEx.False(
            CompanionCommandSession.ShouldExit(false, false, false, false, true),
            "Credential settings companion exited while its window was visible.");
    }

    [KeyinaTest("production command companion enables speech without resident hooks tray or pipe")]
    private static void ProductionOptionsAreOnDemand()
    {
        var options = KeyinaRuntimeOptions.CreateProductionCommandCompanion();

        AssertEx.False(options.EnableNotifyIcon, "Command companion enabled a tray icon.");
        AssertEx.False(options.EnableGlobalHotkeys, "Command companion installed global hooks.");
        AssertEx.False(options.EnablePipe, "Command companion enabled resident IPC.");
        AssertEx.True(options.EnableSpeech, "Command companion disabled speech support.");
        AssertEx.False(options.ShowSettingsOnStart, "Command companion opened settings.");
        AssertEx.False(options.DisplaySettingsWindows, "Command companion displayed settings UI.");
        AssertEx.True(
            options.DisplayTranslationPreview,
            "Command companion hid the translation preview window.");
        AssertEx.False(
            options.PublishRuntimeProfileOnStartup,
            "Command companion could overwrite the native toggle before applying it.");
        AssertEx.NotNull(
            options.FocusedDictationWriterFactory,
            "Command companion did not configure focus-locked dictation delivery.");
    }
}
