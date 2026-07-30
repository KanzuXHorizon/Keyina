using Keyina.Host.Core.Feedback;
using Keyina.Host.Core.Hotkeys;
using Keyina.Host.Runtime;
using Keyina.Host.UI.Feedback;
using Keyina.Host.Windows.Feedback;

namespace Keyina.Host.Tests;

internal static class KeyinaApplicationContextTests
{
    [KeyinaTest("resident context loads configuration toggles input and creates settings lazily")]
    private static void SafeRuntimeLifecycleWorks()
    {
        using var directory = new TemporaryDirectory();
        var options = KeyinaRuntimeOptions.CreateSelfTest(
            Path.Combine(directory.Path, "settings.json"),
            $"Keyina.Tests.{Guid.NewGuid():N}");

        using var context = new KeyinaApplicationContext(options);
        AssertEx.True(context.CurrentState.VietnameseEnabled,
            "Vietnamese input did not start enabled.");
        AssertEx.True(!context.SettingsCreated,
            "Settings form was created before the user opened it.");
        AssertEx.True(!context.NotifyIconVisible,
            "Safe runtime unexpectedly exposed a tray icon.");

        context.DispatchCommandAsync(
                HotkeyCommand.ToggleVietnamese,
                CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(2))
            .GetAwaiter().GetResult();
        AssertEx.True(!context.CurrentState.VietnameseEnabled,
            "Toggle command did not update runtime state.");

        context.OpenSettings();
        AssertEx.True(context.SettingsCreated,
            "Settings form was not created lazily.");
        context.CloseSettings();
        AssertEx.True(!context.SettingsCreated,
            "Closed settings form remained owned by the context.");

        context.Dispose();
        context.Dispose();
    }

    [KeyinaTest("resident context publishes shortcut feedback without changing focus state")]
    private static void ToggleVietnamesePublishesFeedback()
    {
        using var directory = new TemporaryDirectory();
        var overlay = new RecordingOverlay();
        var sound = new RecordingSoundPlayer();
        var options = KeyinaRuntimeOptions.CreateSelfTest(
            Path.Combine(directory.Path, "settings.json"),
            $"Keyina.Tests.{Guid.NewGuid():N}") with
        {
            ForegroundPresentationProbeFactory =
                static () => new FixedForegroundProbe(ForegroundPresentationState.Windowed),
            FeedbackOverlayFactory = () => overlay,
            FeedbackSoundPlayerFactory = () => sound,
        };

        using var context = new KeyinaApplicationContext(options);
        context.DispatchCommandAsync(
                HotkeyCommand.ToggleVietnamese,
                CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.Equal(1, overlay.Events.Count);
        AssertEx.Equal(FeedbackEventKind.VietnameseDisabled, overlay.Events[0].Kind);
        AssertEx.Equal(1, sound.Cues.Count);
        AssertEx.Equal(FeedbackSoundCue.Disabled, sound.Cues[0]);
        AssertEx.Equal(null, context.CurrentState.ErrorCode);
    }

    [KeyinaTest("feedback failures never fail the resident host command")]
    private static void FeedbackFailureIsIsolatedFromRuntimeState()
    {
        using var directory = new TemporaryDirectory();
        var sound = new RecordingSoundPlayer();
        var options = KeyinaRuntimeOptions.CreateSelfTest(
            Path.Combine(directory.Path, "settings.json"),
            $"Keyina.Tests.{Guid.NewGuid():N}") with
        {
            ForegroundPresentationProbeFactory =
                static () => new FixedForegroundProbe(ForegroundPresentationState.Windowed),
            FeedbackOverlayFactory = static () => new ThrowingOverlay(),
            FeedbackSoundPlayerFactory = () => sound,
        };

        using var context = new KeyinaApplicationContext(options);
        context.DispatchCommandAsync(
                HotkeyCommand.ToggleVietnamese,
                CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.False(context.CurrentState.VietnameseEnabled,
            "Feedback failure prevented the input-mode command.");
        AssertEx.Equal(null, context.CurrentState.ErrorCode);
        AssertEx.Equal(1, sound.Cues.Count);
    }

    [KeyinaTest("resident context never reports ready without native TSF and focused typing evidence")]
    private static void RuntimeReadinessIsTruthful()
    {
        using var directory = new TemporaryDirectory();
        var options = KeyinaRuntimeOptions.CreateSelfTest(
            Path.Combine(directory.Path, "settings.json"),
            $"Keyina.Tests.{Guid.NewGuid():N}");
        using var context = new KeyinaApplicationContext(options);

        AssertEx.True(
            context.CurrentSettingsSnapshot.Readiness != Keyina.Host.UI.KeyinaReadiness.Ready,
            "Self-test runtime claimed Ready without TSF, IPC, or typing evidence.");
    }

    [KeyinaTest("resident context exposes complete tray commands without changing global state")]
    private static void TrayMenuContractIsComplete()
    {
        using var directory = new TemporaryDirectory();
        var options = KeyinaRuntimeOptions.CreateSelfTest(
            Path.Combine(directory.Path, "settings.json"),
            $"Keyina.Tests.{Guid.NewGuid():N}");
        using var context = new KeyinaApplicationContext(options);

        var commands = context.TrayCommandNames;
        AssertEx.True(context.TrayUsesCustomRenderer,
            "Tray menu should use the Fluent renderer.");
        AssertEx.True(context.TrayShowsImageMargin,
            "Tray menu should reserve a consistent icon column.");
        AssertEx.True(context.TrayHorizontalPadding >= 8,
            "Tray menu padding is too tight for a modern touch-friendly surface.");
        foreach (var expected in new[]
                 {
                     "status",
                     "setup",
                     "toggleVietnamese",
                     "toggleDictation",
                     "startup",
                     "settings",
                     "exit",
                 })
        {
            AssertEx.True(commands.Contains(expected, StringComparer.Ordinal),
                $"Tray command {expected} was missing.");
        }
    }

    private sealed class FixedForegroundProbe(ForegroundPresentationState state)
        : IForegroundPresentationProbe
    {
        public ForegroundPresentationState GetState() => state;
    }

    private sealed class RecordingOverlay : IFeedbackOverlay
    {
        public List<FeedbackEvent> Events { get; } = [];

        public void Present(FeedbackEvent feedbackEvent) => Events.Add(feedbackEvent);

        public void HideFeedback()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingOverlay : IFeedbackOverlay
    {
        public void Present(FeedbackEvent feedbackEvent) =>
            throw new InvalidOperationException("overlay failed");

        public void HideFeedback()
        {
        }

        public void Dispose()
        {
        }
    }

    private sealed class RecordingSoundPlayer : IFeedbackSoundPlayer
    {
        public List<FeedbackSoundCue> Cues { get; } = [];

        public void Play(FeedbackSoundCue cue) => Cues.Add(cue);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"Keyina.Runtime.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
