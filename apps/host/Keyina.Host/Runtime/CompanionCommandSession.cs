using Keyina.Host.Core.Hotkeys;

namespace Keyina.Host.Runtime;

public enum CompanionCommand
{
    SetVietnameseEnabled,
    SetVietnameseDisabled,
    PushToTalkPressed,
    PushToTalkReleased,
    ToggleDictation,
    TranslateSelection,
    UndoTranslation,
    CancelActiveCommand,
}

public static class CompanionCommandProtocol
{
    public const string MutexName = "Local\\Keyina.CommandCompanion";

    public static bool TryParseArgument(string? argument, out CompanionCommand command)
    {
        const string prefix = "--companion-command=";
        if (argument is null || !argument.StartsWith(prefix, StringComparison.Ordinal))
        {
            command = default;
            return false;
        }

        command = argument[prefix.Length..] switch
        {
            "set-vietnamese-enabled" => CompanionCommand.SetVietnameseEnabled,
            "set-vietnamese-disabled" => CompanionCommand.SetVietnameseDisabled,
            "push-to-talk-pressed" => CompanionCommand.PushToTalkPressed,
            "push-to-talk-released" => CompanionCommand.PushToTalkReleased,
            "toggle-dictation" => CompanionCommand.ToggleDictation,
            "translate-selection" => CompanionCommand.TranslateSelection,
            "undo-translation" => CompanionCommand.UndoTranslation,
            "cancel-active-command" => CompanionCommand.CancelActiveCommand,
            _ => default,
        };
        return argument[prefix.Length..] is
            "set-vietnamese-enabled" or
            "set-vietnamese-disabled" or
            "push-to-talk-pressed" or
            "push-to-talk-released" or
            "toggle-dictation" or
            "translate-selection" or
            "undo-translation" or
            "cancel-active-command";
    }

    public static string ToArgument(CompanionCommand command) =>
        $"--companion-command={ToToken(command)}";

    public static string EventName(CompanionCommand command) =>
        $"Local\\Keyina.Command.{ToToken(command)}";

    public static HotkeyCommand ToHotkeyCommand(CompanionCommand command) => command switch
    {
        CompanionCommand.SetVietnameseEnabled or
        CompanionCommand.SetVietnameseDisabled =>
            throw new ArgumentException(
                "Vietnamese state commands are applied directly.",
                nameof(command)),
        CompanionCommand.PushToTalkPressed => HotkeyCommand.PushToTalkPressed,
        CompanionCommand.PushToTalkReleased => HotkeyCommand.PushToTalkReleased,
        CompanionCommand.ToggleDictation => HotkeyCommand.ToggleDictation,
        CompanionCommand.TranslateSelection => HotkeyCommand.TranslateSelection,
        CompanionCommand.UndoTranslation => HotkeyCommand.UndoTranslation,
        CompanionCommand.CancelActiveCommand => HotkeyCommand.CancelDictation,
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };

    private static string ToToken(CompanionCommand command) => command switch
    {
        CompanionCommand.SetVietnameseEnabled => "set-vietnamese-enabled",
        CompanionCommand.SetVietnameseDisabled => "set-vietnamese-disabled",
        CompanionCommand.PushToTalkPressed => "push-to-talk-pressed",
        CompanionCommand.PushToTalkReleased => "push-to-talk-released",
        CompanionCommand.ToggleDictation => "toggle-dictation",
        CompanionCommand.TranslateSelection => "translate-selection",
        CompanionCommand.UndoTranslation => "undo-translation",
        CompanionCommand.CancelActiveCommand => "cancel-active-command",
        _ => throw new ArgumentOutOfRangeException(nameof(command)),
    };
}

public sealed class CompanionCommandSession : IDisposable
{
    private static readonly TimeSpan SignalRetryWindow = TimeSpan.FromSeconds(2);
    private readonly KeyinaApplicationContext context;
    private readonly Control dispatcher = new();
    private readonly System.Windows.Forms.Timer idleTimer = new()
    {
        Interval = 1_000,
    };
    private readonly List<EventWaitHandle> events = [];
    private readonly List<RegisteredWaitHandle> registrations = [];
    private int pendingCommands;
    private bool disposed;

    public CompanionCommandSession(KeyinaApplicationContext context)
    {
        this.context = context ?? throw new ArgumentNullException(nameof(context));
        dispatcher.CreateControl();
        foreach (var command in Enum.GetValues<CompanionCommand>())
        {
            var signal = new EventWaitHandle(
                initialState: false,
                EventResetMode.AutoReset,
                CompanionCommandProtocol.EventName(command));
            events.Add(signal);
            registrations.Add(ThreadPool.RegisterWaitForSingleObject(
                signal,
                (_, _) => Post(command),
                state: null,
                Timeout.InfiniteTimeSpan,
                executeOnlyOnce: false));
        }

        idleTimer.Tick += (_, _) => ExitIfIdle();
        idleTimer.Start();
    }

    public void Post(CompanionCommand command)
    {
        if (disposed || dispatcher.IsDisposed)
        {
            return;
        }
        Interlocked.Increment(ref pendingCommands);
        try
        {
            dispatcher.BeginInvoke(async () =>
            {
                try
                {
                    if (disposed)
                    {
                        return;
                    }
                    if (command is CompanionCommand.SetVietnameseEnabled or
                        CompanionCommand.SetVietnameseDisabled)
                    {
                        await context.ApplyNativeVietnameseStateAsync(
                                command == CompanionCommand.SetVietnameseEnabled,
                                CancellationToken.None)
                            .ConfigureAwait(true);
                    }
                    else
                    {
                        await context.DispatchCommandAsync(
                                CompanionCommandProtocol.ToHotkeyCommand(command),
                                CancellationToken.None)
                            .ConfigureAwait(true);
                    }
                }
                catch (Exception)
                {
                    // Commands are best effort. The application context reports
                    // stable failure codes without exposing user content.
                }
                finally
                {
                    Interlocked.Decrement(ref pendingCommands);
                    ExitIfIdle();
                }
            });
        }
        catch (InvalidOperationException)
        {
            Interlocked.Decrement(ref pendingCommands);
        }
    }

    public static bool ShouldExit(
        bool commandInFlight,
        bool dictationActive,
        bool canUndoTranslation,
        bool translationPreviewCreated,
        bool interactiveWindowCreated) =>
        !commandInFlight &&
        !dictationActive &&
        !canUndoTranslation &&
        !translationPreviewCreated &&
        !interactiveWindowCreated;

    private void ExitIfIdle()
    {
        if (!disposed && ShouldExit(
                commandInFlight: Volatile.Read(ref pendingCommands) != 0,
                context.IsDictationActive,
                context.CanUndoTranslation,
                context.TranslationPreviewCreated,
                context.SettingsCreated || context.FirstRunCreated))
        {
            context.ExitThread();
        }
    }

    public static bool SignalExisting(CompanionCommand command)
    {
        var deadline = DateTime.UtcNow + SignalRetryWindow;
        do
        {
            try
            {
                using var signal = EventWaitHandle.OpenExisting(
                    CompanionCommandProtocol.EventName(command));
                return signal.Set();
            }
            catch (WaitHandleCannotBeOpenedException)
            {
                Thread.Sleep(25);
            }
        }
        while (DateTime.UtcNow < deadline);

        return false;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        idleTimer.Stop();
        idleTimer.Dispose();
        foreach (var registration in registrations)
        {
            registration.Unregister(waitObject: null);
        }
        registrations.Clear();
        foreach (var signal in events)
        {
            signal.Dispose();
        }
        events.Clear();
        dispatcher.Dispose();
    }
}
