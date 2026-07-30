using System.Collections.Concurrent;
using System.Diagnostics;
using Keyina.Host.Configuration;
using Keyina.Host.Core;
using Keyina.Host.Core.Configuration;
using Keyina.Host.Core.Feedback;
using Keyina.Host.Core.Hotkeys;
using Keyina.Host.Core.Ipc;
using Keyina.Host.Core.Speech;
using Keyina.Host.Speech;
using Keyina.Host.UI;
using Keyina.Host.UI.Feedback;
using Keyina.Host.UI.Fluent;
using Keyina.Host.Windows.Audio;
using Keyina.Host.Windows.Credentials;
using Keyina.Host.Windows.Feedback;
using Keyina.Host.Windows.Hotkeys;
using Keyina.Host.Windows.Ipc;
using Keyina.Host.Windows.Startup;
using Keyina.Host.Windows.Typing;

namespace Keyina.Host.Runtime;

public sealed class KeyinaApplicationContext : ApplicationContext
{
    private const int PushToTalkHotkeyId = 1;
    private const int ToggleDictationHotkeyId = 2;
    private const int CancelDictationHotkeyId = 3;

    private readonly KeyinaRuntimeOptions options;
    private readonly AtomicConfigurationStore configurationStore;
    private readonly WindowsStartupRegistration startupRegistration;
    private readonly WindowsCredentialVault credentialVault = new();
    private readonly TsfSetupService tsfSetupService = new();
    private readonly ConcurrentQueue<Action> pendingSignals = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim speechCommandGate = new(1, 1);
    private readonly Control dispatcher = new();
    private readonly ContextMenuStrip trayMenu = new();
    private readonly ToolStripMenuItem statusMenuItem;
    private readonly ToolStripMenuItem setupMenuItem;
    private readonly ToolStripMenuItem toggleVietnameseMenuItem;
    private readonly ToolStripMenuItem toggleDictationMenuItem;
    private readonly ToolStripMenuItem startupMenuItem;
    private readonly ToolStripMenuItem settingsMenuItem;
    private readonly ToolStripMenuItem exitMenuItem;
    private readonly NotifyIcon notifyIcon;
    private readonly Icon activeIcon;
    private readonly Icon inactiveIcon;
    private readonly Icon listeningIcon;

    private KeyinaConfiguration configuration;
    private HostState state;
    private HotkeyMessageWindow? hotkeyWindow;
    private RegisteredHotkeyManager? hotkeyManager;
    private ModifierKeyboardHook? modifierHook;
    private VietnameseKeyboardHook? typingHook;
    private NamedPipeEnvelopeServer? pipeServer;
    private DictationCoordinator? dictationCoordinator;
    private DictationOverlayModel? dictationOverlay;
    private FeedbackCoordinator? feedbackCoordinator;
    private SettingsForm? settingsForm;
    private int drainScheduled;
    private bool hotkeysReady;
    private bool typingReady;
    private bool pipeReady;
    private bool endToEndTypingPassed;
    private DateTimeOffset? lastTypingTestAt;
    private FluentThemeMode? trayThemeMode;
    private DictationStatus? lastFeedbackDictationStatus;
    private bool disposed;

    public KeyinaApplicationContext(KeyinaRuntimeOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        options.Validate();

        configurationStore = new AtomicConfigurationStore(options.ConfigurationPath);
        configuration = LoadConfiguration();
        state = HostState.Initial with
        {
            VietnameseEnabled = configuration.VietnameseEnabled,
        };
        startupRegistration = new WindowsStartupRegistration(
            options.StartupValueName,
            static () => Environment.ProcessPath ??
                throw new InvalidOperationException(
                    "The current process executable path is unavailable."));

        activeIcon = LoadIcon("keyina-tray-active.ico");
        inactiveIcon = LoadIcon("keyina-tray-inactive.ico");
        listeningIcon = LoadIcon("keyina-tray-listening.ico");

        statusMenuItem = new ToolStripMenuItem
        {
            Name = "status",
            Enabled = false,
            Font = new Font("Segoe UI Variable Text", 9.5F, FontStyle.Bold),
            Tag = "header",
            AccessibleName = "Trạng thái Keyina",
        };
        setupMenuItem = new ToolStripMenuItem
        {
            Name = "setup",
            Text = "Thiết lập bộ gõ…",
            AccessibleName = "Thiết lập hoặc sửa bộ gõ",
        };
        toggleVietnameseMenuItem = new ToolStripMenuItem
        {
            Name = "toggleVietnamese",
            ShortcutKeyDisplayString = "Ctrl+Shift",
            AccessibleName = "Bật hoặc tắt bộ gõ tiếng Việt",
        };
        toggleDictationMenuItem = new ToolStripMenuItem
        {
            Name = "toggleDictation",
            ShortcutKeyDisplayString = "Ctrl+Alt+V",
            AccessibleName = "Bắt đầu hoặc dừng nhập bằng giọng nói",
        };
        startupMenuItem = new ToolStripMenuItem
        {
            Name = "startup",
            Text = "Khởi động cùng Windows",
            CheckOnClick = false,
            AccessibleName = "Khởi động Keyina cùng Windows",
        };
        settingsMenuItem = new ToolStripMenuItem
        {
            Name = "settings",
            Text = "Mở cài đặt",
            AccessibleName = "Mở cài đặt Keyina",
        };
        exitMenuItem = new ToolStripMenuItem
        {
            Name = "exit",
            Text = "Thoát Keyina",
            Tag = "danger",
            AccessibleName = "Thoát Keyina",
        };

        trayMenu.Items.AddRange(
        [
            statusMenuItem,
            new ToolStripSeparator(),
            setupMenuItem,
            toggleVietnameseMenuItem,
            toggleDictationMenuItem,
            new ToolStripSeparator(),
            startupMenuItem,
            settingsMenuItem,
            new ToolStripSeparator(),
            exitMenuItem,
        ]);
        trayMenu.Opening += (_, _) =>
        {
            ApplyTrayTheme();
            RefreshVisualState();
            FluentTrayMenu.ApplyRoundedRegion(trayMenu);
        };
        trayMenu.Opened += (_, _) =>
            FluentWindow.ApplyTransient(trayMenu, FluentTheme.Current);

        setupMenuItem.Click += (_, _) => OpenSettings();
        toggleVietnameseMenuItem.Click += (_, _) =>
            PostCommand(HotkeyCommand.ToggleVietnamese);
        toggleDictationMenuItem.Click += (_, _) =>
            PostCommand(HotkeyCommand.ToggleDictation);
        startupMenuItem.Click += (_, _) =>
            SetStartupEnabled(!startupRegistration.IsEnabled);
        settingsMenuItem.Click += (_, _) => OpenSettings();
        exitMenuItem.Click += (_, _) => ExitThread();

        notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = trayMenu,
            Icon = activeIcon,
            Text = "Keyina — Bộ gõ tiếng Việt đang bật",
            Visible = options.EnableNotifyIcon,
        };
        notifyIcon.DoubleClick += (_, _) => OpenSettings();
        ApplyTrayTheme();

        dispatcher.CreateControl();
        InitializeFeedbackRuntime();
        InitializeOptionalRuntime();
        RefreshVisualState();
        if (options.ShowSettingsOnStart)
        {
            OpenSettings();
        }
    }

    public HostState CurrentState => state;

    public SettingsSnapshot CurrentSettingsSnapshot => CreateSettingsSnapshot();

    public bool SettingsCreated => settingsForm is not null && !settingsForm.IsDisposed;

    public bool NotifyIconVisible => notifyIcon.Visible;

    public bool TrayUsesCustomRenderer => trayMenu.Renderer is FluentTrayRenderer;

    public bool TrayShowsImageMargin => trayMenu.ShowImageMargin;

    public int TrayHorizontalPadding => trayMenu.Padding.Horizontal;

    public IReadOnlyList<string> TrayCommandNames => trayMenu.Items
        .OfType<ToolStripItem>()
        .Select(item => item.Name)
        .Where(name => !string.IsNullOrEmpty(name))
        .Select(name => name!)
        .ToArray();

    public async Task DispatchCommandAsync(
        HotkeyCommand command,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        switch (command)
        {
            case HotkeyCommand.None:
                return;
            case HotkeyCommand.ToggleVietnamese:
                await SetVietnameseEnabledAsync(
                        !state.VietnameseEnabled,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            case HotkeyCommand.PushToTalkPressed:
                await StartDictationAsync(cancellationToken).ConfigureAwait(true);
                return;
            case HotkeyCommand.PushToTalkReleased:
                await StopDictationAsync(cancellationToken).ConfigureAwait(true);
                return;
            case HotkeyCommand.ToggleDictation:
                if (IsDictationActive)
                {
                    await StopDictationAsync(cancellationToken).ConfigureAwait(true);
                }
                else
                {
                    await StartDictationAsync(cancellationToken).ConfigureAwait(true);
                }
                return;
            case HotkeyCommand.CancelDictation:
                await CancelDictationAsync().ConfigureAwait(true);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }
    }

    public void OpenSettings()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (SettingsCreated)
        {
            if (settingsForm!.WindowState == FormWindowState.Minimized)
            {
                settingsForm.WindowState = FormWindowState.Normal;
            }
            settingsForm.Show();
            settingsForm.Activate();
            return;
        }

        settingsForm = new SettingsForm(CreateSettingsSnapshot(), CreateSettingsActions());
        settingsForm.FormClosed += (_, _) => settingsForm = null;
        if (options.DisplaySettingsWindows)
        {
            settingsForm.Show();
        }
    }

    public void CloseSettings()
    {
        if (SettingsCreated)
        {
            settingsForm!.Close();
        }
    }

    protected override void ExitThreadCore()
    {
        DisposeRuntime();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeRuntime();
        }
        base.Dispose(disposing);
    }

    private bool IsDictationActive => dictationCoordinator?.State.Status is
        DictationStatus.Connecting or
        DictationStatus.Listening or
        DictationStatus.Finalizing;

    private KeyinaConfiguration LoadConfiguration()
    {
        try
        {
            return configurationStore.LoadAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch (ConfigurationException)
        {
            return KeyinaConfiguration.Default;
        }
    }

    private void InitializeFeedbackRuntime()
    {
        try
        {
            var foregroundProbe = options.ForegroundPresentationProbeFactory?.Invoke()
                ?? new WindowsForegroundPresentationProbe();
            var overlay = options.FeedbackOverlayFactory?.Invoke()
                ?? new NoActivateFeedbackOverlay();
            var soundPlayer = options.FeedbackSoundPlayerFactory?.Invoke()
                ?? new WindowsFeedbackSoundPlayer();
            feedbackCoordinator = new FeedbackCoordinator(
                configuration.Feedback ?? FeedbackPreferences.Default,
                foregroundProbe,
                overlay,
                soundPlayer);
        }
        catch (Exception exception)
        {
            feedbackCoordinator = null;
            if (options.ForegroundPresentationProbeFactory is not null ||
                options.FeedbackOverlayFactory is not null ||
                options.FeedbackSoundPlayerFactory is not null)
            {
                throw new InvalidOperationException(
                    "Injected feedback runtime failed to initialize.",
                    exception);
            }
        }
    }

    private void InitializeOptionalRuntime()
    {
        if (options.EnablePipe)
        {
            try
            {
                pipeServer = new NamedPipeEnvelopeServer(
                    PipeEndpointName.ForCurrentSession());
                pipeServer.StartAsync(lifetime.Token).GetAwaiter().GetResult();
                pipeReady = true;
            }
            catch (Exception)
            {
                pipeReady = false;
                ReportFailure("ipc_start_failed");
            }
        }

        if (options.EnableGlobalHotkeys)
        {
            try
            {
                hotkeyWindow = new HotkeyMessageWindow();
                hotkeyManager = new RegisteredHotkeyManager(
                    nativeApi: null,
                    hotkeyWindow.Handle);
                hotkeyWindow.HotkeyReceived += (_, id) =>
                    _ = hotkeyManager.TryDispatch(id);
                hotkeyManager.CommandReceived += (_, command) => PostCommand(command);
                hotkeyWindow.Invoke(() => hotkeyManager.Register(CreateRegisteredBindings()));

                modifierHook = new ModifierKeyboardHook();
                modifierHook.CommandReceived += (_, command) => PostCommand(command);
                modifierHook.Start();

                typingHook = new VietnameseKeyboardHook();
                typingHook.Start(configuration.VietnameseEnabled);
                typingReady = true;
                hotkeysReady = true;
            }
            catch (Exception)
            {
                typingHook?.Dispose();
                typingHook = null;
                typingReady = false;
                hotkeysReady = false;
                ReportFailure("hotkey_start_failed");
            }
        }

        if (options.EnableSpeech && pipeServer is not null)
        {
            dictationOverlay = new DictationOverlayModel();
            dictationOverlay.StateChanged += (_, _) =>
                PostSignal(HandleDictationStateChanged);
            dictationCoordinator = new DictationCoordinator(
                new SpeechmaticsSessionFactory(),
                new WasapiMicrophoneCapture(),
                new ActivePipeEnvelopeWriter(pipeServer),
                dictationOverlay,
                hostEvent => PostSignal(() => ApplyHostEvent(hostEvent)),
                () => pipeServer.ActiveTarget?.FocusGeneration ?? 0,
                TimeSpan.FromSeconds(5));
        }
    }

    private static RegisteredHotkeyBinding[] CreateRegisteredBindings() =>
    [
        new(
            PushToTalkHotkeyId,
            new HotkeyChord(
                HotkeyModifiers.Control | HotkeyModifiers.Alt,
                VirtualKey.Space),
            HotkeyCommand.PushToTalkPressed),
        new(
            ToggleDictationHotkeyId,
            new HotkeyChord(
                HotkeyModifiers.Control | HotkeyModifiers.Alt,
                VirtualKey.V),
            HotkeyCommand.ToggleDictation),
        new(
            CancelDictationHotkeyId,
            new HotkeyChord(HotkeyModifiers.None, VirtualKey.Escape),
            HotkeyCommand.CancelDictation),
    ];

    private void PostCommand(HotkeyCommand command) =>
        PostSignal(() => _ = ExecuteCommandSafelyAsync(command));

    private async Task ExecuteCommandSafelyAsync(HotkeyCommand command)
    {
        try
        {
            await DispatchCommandAsync(command, lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            ReportFailure("command_failed");
        }
    }

    private void PostSignal(Action action)
    {
        if (disposed)
        {
            return;
        }
        pendingSignals.Enqueue(action);
        if (Interlocked.Exchange(ref drainScheduled, 1) != 0)
        {
            return;
        }
        try
        {
            dispatcher.BeginInvoke(DrainSignals);
        }
        catch (InvalidOperationException)
        {
            Interlocked.Exchange(ref drainScheduled, 0);
        }
    }

    private void DrainSignals()
    {
        while (pendingSignals.TryDequeue(out var action))
        {
            try
            {
                action();
            }
            catch (Exception)
            {
                ReportFailure("runtime_signal_failed");
            }
        }
        Interlocked.Exchange(ref drainScheduled, 0);
        if (!pendingSignals.IsEmpty &&
            Interlocked.Exchange(ref drainScheduled, 1) == 0)
        {
            dispatcher.BeginInvoke(DrainSignals);
        }
    }

    private async Task SetVietnameseEnabledAsync(
        bool enabled,
        CancellationToken cancellationToken)
    {
        state = HostReducer.Reduce(state, new InputModeChanged(enabled));
        configuration = configuration with { VietnameseEnabled = enabled };
        typingHook?.SetEnabled(enabled);
        RefreshVisualState();
        PublishFeedback(FeedbackEvents.ForVietnamese(enabled));

        if (pipeServer?.ActiveTarget is { } target)
        {
            var envelope = new IpcEnvelope(
                IpcMessageType.ToggleInput,
                Flags: 0,
                target.SessionId,
                target.FocusGeneration,
                enabled ? "enabled" : "disabled");
            _ = await pipeServer.WriteToActiveAsync(envelope, cancellationToken)
                .ConfigureAwait(false);
        }
        await SaveConfigurationAsync(cancellationToken).ConfigureAwait(false);
    }

    private void SetSpeechEnabled(bool enabled)
    {
        configuration = configuration with { SpeechEnabled = enabled };
        _ = SaveConfigurationSafelyAsync();
        RefreshVisualState();
    }

    private void SetStartupEnabled(bool enabled)
    {
        try
        {
            startupRegistration.SetEnabled(enabled);
            RecoverHost();
        }
        catch (Exception)
        {
            ReportFailure("startup_update_failed");
        }
        RefreshVisualState();
    }

    private void SetFeedbackMode(FeedbackMode mode)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        var preferences = new FeedbackPreferences(mode);
        configuration = configuration with { Feedback = preferences };
        try
        {
            feedbackCoordinator?.UpdatePreferences(preferences);
        }
        catch (Exception)
        {
            // Feedback configuration remains persistent even if presentation is unavailable.
        }
        _ = SaveConfigurationSafelyAsync();
        RefreshVisualState();
    }

    private void PreviewFeedback() => PublishFeedback(FeedbackEvents.Preview());

    private async Task StartDictationAsync(CancellationToken cancellationToken)
    {
        await speechCommandGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (dictationCoordinator is null || !configuration.SpeechEnabled)
            {
                ReportFailure("speech_disabled");
                PublishFeedback(FeedbackEvents.Error("Nhập giọng nói đang tắt"));
                return;
            }
            if (IsDictationActive)
            {
                return;
            }
            var target = pipeServer?.ActiveTarget;
            if (target is null)
            {
                ReportFailure("speech_no_focused_app");
                PublishFeedback(FeedbackEvents.Error("Chưa có ứng dụng nhận văn bản"));
                return;
            }
            var apiKey = credentialVault.Read(CredentialTargets.SpeechmaticsApiKey);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                ReportFailure("speech_credential_missing");
                PublishFeedback(FeedbackEvents.Error("Chưa cấu hình khóa Speechmatics"));
                return;
            }

            RecoverHost();
            await dictationCoordinator.StartAsync(
                    apiKey,
                    target.SessionId,
                    cancellationToken)
                .ConfigureAwait(true);
        }
        catch (Exception)
        {
            ReportFailure("speech_start_failed");
            PublishFeedback(FeedbackEvents.Error("Không thể bắt đầu nhập giọng nói"));
        }
        finally
        {
            speechCommandGate.Release();
            RefreshVisualState();
        }
    }

    private async Task StopDictationAsync(CancellationToken cancellationToken)
    {
        await speechCommandGate.WaitAsync(cancellationToken).ConfigureAwait(true);
        try
        {
            if (dictationCoordinator is not null && IsDictationActive)
            {
                await dictationCoordinator.StopAsync(cancellationToken)
                    .ConfigureAwait(true);
            }
        }
        catch (Exception)
        {
            ReportFailure("speech_stop_failed");
            PublishFeedback(FeedbackEvents.Error("Không thể hoàn tất nhập giọng nói"));
        }
        finally
        {
            speechCommandGate.Release();
            RefreshVisualState();
        }
    }

    private async Task CancelDictationAsync()
    {
        await speechCommandGate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (dictationCoordinator is not null)
            {
                await dictationCoordinator.CancelAsync().ConfigureAwait(true);
            }
        }
        catch (Exception)
        {
            ReportFailure("speech_cancel_failed");
            PublishFeedback(FeedbackEvents.Error("Không thể hủy nhập giọng nói"));
        }
        finally
        {
            speechCommandGate.Release();
            RefreshVisualState();
        }
    }

    private void ApplyHostEvent(HostEvent hostEvent)
    {
        state = HostReducer.Reduce(state, hostEvent);
        RefreshVisualState();
    }

    private void HandleDictationStateChanged()
    {
        RefreshVisualState();
        if (dictationOverlay is null)
        {
            return;
        }

        var status = dictationOverlay.State.Status;
        if (lastFeedbackDictationStatus == status)
        {
            return;
        }

        lastFeedbackDictationStatus = status;
        var feedbackEvent = FeedbackEvents.ForDictation(status);
        if (feedbackEvent is not null)
        {
            PublishFeedback(feedbackEvent);
        }
    }

    private void PublishFeedback(FeedbackEvent feedbackEvent)
    {
        try
        {
            feedbackCoordinator?.Publish(feedbackEvent);
        }
        catch (Exception)
        {
            // Feedback is best-effort and must not affect the originating command.
        }
    }

    private void ReportFailure(string errorCode)
    {
        state = HostReducer.Reduce(state, new HostFailed(errorCode));
        RefreshVisualState();
    }

    private void RecoverHost()
    {
        if (state.ErrorCode is not null)
        {
            state = HostReducer.Reduce(state, new HostRecovered());
        }
    }

    private SettingsActions CreateSettingsActions() => new(
        enabled => _ = SetVietnameseEnabledSafelyAsync(enabled),
        SetSpeechEnabled,
        SetStartupEnabled,
        secret =>
        {
            try
            {
                credentialVault.Write(CredentialTargets.SpeechmaticsApiKey, secret);
                RecoverHost();
            }
            catch (Exception)
            {
                ReportFailure("credential_write_failed");
            }
            RefreshVisualState();
        },
        () =>
        {
            try
            {
                _ = credentialVault.Delete(CredentialTargets.SpeechmaticsApiKey);
            }
            catch (Exception)
            {
                ReportFailure("credential_delete_failed");
            }
            RefreshVisualState();
        },
        OpenConfigurationFolder,
        RunDiagnosticsAsync,
        SetupTsfAsync,
        RecordTypingTest,
        SetTypingInstrumentationEnabled,
        TypingLatencyProfiler.Snapshot,
        ClearTypingInstrumentation,
        SetFeedbackMode,
        PreviewFeedback);

    private static void SetTypingInstrumentationEnabled(bool enabled)
    {
        TypingLatencyProfiler.SetEnabled(enabled);
        if (enabled)
        {
            TypingTraceBuffer.Clear();
            TypingTraceBuffer.SetEnabled(true);
        }
        else
        {
            TypingTraceBuffer.SetEnabled(false);
        }
    }

    private static void ClearTypingInstrumentation()
    {
        var traceEnabled = TypingTraceBuffer.IsEnabled;
        TypingLatencyProfiler.Clear();
        TypingTraceBuffer.Clear();
        if (traceEnabled)
        {
            TypingTraceBuffer.SetEnabled(true);
        }
    }

    private async Task SetVietnameseEnabledSafelyAsync(bool enabled)
    {
        try
        {
            await SetVietnameseEnabledAsync(enabled, lifetime.Token)
                .ConfigureAwait(true);
        }
        catch (Exception)
        {
            ReportFailure("input_toggle_failed");
        }
    }

    private SettingsSnapshot CreateSettingsSnapshot()
    {
        bool credentialConfigured;
        try
        {
            credentialConfigured = !string.IsNullOrWhiteSpace(
                credentialVault.Read(CredentialTargets.SpeechmaticsApiKey));
        }
        catch (Exception)
        {
            credentialConfigured = false;
        }

        var ipcConnected = pipeReady && pipeServer?.ActiveTarget is not null;
        var health = new KeyinaHealthSnapshot(
            OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041),
            typingReady,
            ComRegistered: true,
            TsfProfileRegistered: true,
            state.ErrorCode is null,
            IpcConnected: ipcConnected,
            EndToEndTypingPassed: endToEndTypingPassed,
            lastTypingTestAt,
            FocusedApplication: null,
            state.ErrorCode);
        var statusMessage = ReadinessMapper.Map(health) == KeyinaReadiness.NeedsAttention &&
            health.FailureCode is not null
                ? $"{ReadinessMapper.Title(health)} · {health.FailureCode}"
                : ReadinessMapper.Title(health);

        return new SettingsSnapshot(
            state.VietnameseEnabled,
            configuration.SpeechEnabled,
            startupRegistration.IsEnabled,
            state.Listening,
            credentialConfigured,
            configuration.Snippets.Length,
            statusMessage,
            BuildInfo.ProductVersion,
            ipcConnected
                ? "Đã kết nối ứng dụng đang nhập"
                : pipeReady
                    ? "Đang chờ ứng dụng tương thích"
                    : "IPC chưa hoạt động",
            typingReady && hotkeysReady
                ? "Hook bàn phím đang hoạt động"
                : "Hook bàn phím chưa khả dụng",
            typingReady,
            configuration.Feedback?.Mode ?? FeedbackMode.Automatic)
        {
            Health = health,
        };
    }

    private async Task<string> SetupTsfAsync(CancellationToken cancellationToken)
    {
        var result = await tsfSetupService.InstallDeveloperBuildAsync(cancellationToken)
            .ConfigureAwait(true);
        RefreshVisualState();
        return result;
    }

    private void RecordTypingTest(bool passed)
    {
        endToEndTypingPassed = passed;
        lastTypingTestAt = passed ? DateTimeOffset.UtcNow : null;
        RefreshVisualState();
    }

    private void RefreshVisualState()
    {
        var settingsSnapshot = CreateSettingsSnapshot();
        var trayState = state.TrayState;
        var readinessBlocksInput = settingsSnapshot.Readiness is
            KeyinaReadiness.NeedsSetup or
            KeyinaReadiness.NeedsAttention or
            KeyinaReadiness.Unavailable;
        notifyIcon.Icon = trayState switch
        {
            TrayState.Listening => listeningIcon,
            _ when readinessBlocksInput => inactiveIcon,
            TrayState.VietnameseOn => activeIcon,
            TrayState.VietnameseOff => inactiveIcon,
            TrayState.Error => inactiveIcon,
            _ => inactiveIcon,
        };
        notifyIcon.Text = state.Listening
            ? "Keyina — Đang nghe"
            : settingsSnapshot.Readiness switch
            {
                KeyinaReadiness.Ready when state.VietnameseEnabled =>
                    "Keyina — Bộ gõ tiếng Việt đang bật",
                KeyinaReadiness.Ready => "Keyina — Bộ gõ tiếng Việt đang tắt",
                KeyinaReadiness.NeedsSetup => "Keyina — Cần thiết lập TSF",
                KeyinaReadiness.NeedsAttention => "Keyina — Cần xử lý",
                KeyinaReadiness.Unavailable => "Keyina — Không khả dụng",
                _ => "Keyina",
            };
        statusMenuItem.Text = GetTrayStatusText(settingsSnapshot);
        setupMenuItem.Visible = settingsSnapshot.Readiness != KeyinaReadiness.Ready;
        setupMenuItem.Text = settingsSnapshot.Readiness switch
        {
            KeyinaReadiness.NeedsSetup => "Thiết lập bộ gõ…",
            KeyinaReadiness.NeedsAttention => "Sửa kết nối…",
            KeyinaReadiness.Unavailable => "Mở chẩn đoán…",
            _ => "Kiểm tra bộ gõ…",
        };
        toggleVietnameseMenuItem.Text = state.VietnameseEnabled
            ? "Tắt bộ gõ tiếng Việt"
            : "Bật bộ gõ tiếng Việt";
        toggleVietnameseMenuItem.Checked = state.VietnameseEnabled;
        toggleDictationMenuItem.Text = IsDictationActive
            ? "Dừng nhập bằng giọng nói"
            : "Bắt đầu nhập bằng giọng nói";
        toggleDictationMenuItem.Enabled = configuration.SpeechEnabled;
        startupMenuItem.Checked = startupRegistration.IsEnabled;
        if (notifyIcon.Icon is { } trayIcon)
        {
            ReplaceMenuImage(statusMenuItem, trayIcon.ToBitmap());
        }
        if (SettingsCreated)
        {
            settingsForm!.ApplySnapshot(settingsSnapshot);
        }
    }

    private void ApplyTrayTheme()
    {
        var palette = FluentTheme.Current;
        if (trayThemeMode == palette.Mode)
        {
            return;
        }

        FluentTrayMenu.Apply(trayMenu, palette);
        ReplaceMenuImage(setupMenuItem, FluentTrayMenu.CreateGlyph("\uE90F", palette, FluentTone.Warning));
        ReplaceMenuImage(toggleVietnameseMenuItem, FluentTrayMenu.CreateGlyph("\uE765", palette));
        ReplaceMenuImage(toggleDictationMenuItem, FluentTrayMenu.CreateGlyph("\uE720", palette));
        ReplaceMenuImage(startupMenuItem, FluentTrayMenu.CreateGlyph("\uE7E8", palette));
        ReplaceMenuImage(settingsMenuItem, FluentTrayMenu.CreateGlyph("\uE713", palette));
        ReplaceMenuImage(exitMenuItem, FluentTrayMenu.CreateGlyph("\uE7E8", palette, FluentTone.Error));
        if (notifyIcon.Icon is { } trayIcon)
        {
            ReplaceMenuImage(statusMenuItem, trayIcon.ToBitmap());
        }
        trayThemeMode = palette.Mode;
    }

    private static string GetTrayStatusText(SettingsSnapshot snapshot)
    {
        if (snapshot.Listening)
        {
            return "Keyina · Đang nghe";
        }

        return snapshot.Readiness switch
        {
            KeyinaReadiness.Ready when snapshot.VietnameseEnabled =>
                "Keyina · Bộ gõ đang bật",
            KeyinaReadiness.Ready => "Keyina · Bộ gõ đang tắt",
            KeyinaReadiness.NeedsSetup => "Keyina · Cần thiết lập",
            KeyinaReadiness.NeedsAttention => "Keyina · Cần xử lý",
            KeyinaReadiness.Unavailable => "Keyina · Không khả dụng",
            _ => "Keyina · Đang kiểm tra",
        };
    }

    private static void ReplaceMenuImage(ToolStripItem item, Image image)
    {
        var previous = item.Image;
        item.Image = image;
        previous?.Dispose();
    }

    private async Task SaveConfigurationAsync(CancellationToken cancellationToken)
    {
        await configurationStore.SaveAsync(configuration, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task SaveConfigurationSafelyAsync()
    {
        try
        {
            await SaveConfigurationAsync(lifetime.Token).ConfigureAwait(true);
        }
        catch (Exception)
        {
            ReportFailure("configuration_save_failed");
        }
    }

    private void OpenConfigurationFolder()
    {
        try
        {
            var directory = Path.GetDirectoryName(options.ConfigurationPath)
                ?? throw new InvalidOperationException(
                    "Configuration directory is unavailable.");
            Directory.CreateDirectory(directory);
            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true,
            });
        }
        catch (Exception)
        {
            ReportFailure("configuration_folder_failed");
        }
    }

    private async Task<string> RunDiagnosticsAsync(CancellationToken cancellationToken)
    {
        var snapshot = await Diagnostics.HostResourceProbe.CaptureAsync(
                TimeSpan.FromSeconds(1),
                cancellationToken)
            .ConfigureAwait(true);
        var pipe = pipeReady
            ? pipeServer?.ActiveTarget is null
                ? "IPC waiting"
                : "IPC connected"
            : "IPC unavailable";
        var hotkeys = hotkeysReady ? "hotkeys ready" : "hotkeys unavailable";
        var configurationDirectory = Path.GetDirectoryName(options.ConfigurationPath)
            ?? throw new InvalidOperationException("Configuration directory is unavailable.");
        var tracePath = TypingTraceBuffer.WriteSnapshot(
            Path.Combine(configurationDirectory, "diagnostics", "typing-trace.log"));
        return $"{pipe}; {hotkeys}; idle CPU {snapshot.AverageCpuPercent:F2}%; " +
               $"working set {snapshot.WorkingSetBytes / 1024d / 1024d:F1} MiB; " +
               $"typing trace: {tracePath}.";
    }

    private static Icon LoadIcon(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", fileName);
        if (File.Exists(path))
        {
            return new Icon(path);
        }
        return (Icon)SystemIcons.Application.Clone();
    }

    private void DisposeRuntime()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        lifetime.Cancel();

        CloseSettings();
        notifyIcon.Visible = false;

        feedbackCoordinator?.Dispose();
        feedbackCoordinator = null;
        typingHook?.Dispose();
        typingHook = null;
        modifierHook?.Dispose();
        modifierHook = null;
        if (hotkeyManager is not null && hotkeyWindow is not null)
        {
            try
            {
                hotkeyWindow.Invoke(hotkeyManager.Dispose);
            }
            catch (Exception)
            {
                hotkeyManager.Dispose();
            }
        }
        hotkeyManager = null;
        hotkeyWindow?.Dispose();
        hotkeyWindow = null;

        if (dictationCoordinator is not null)
        {
            dictationCoordinator.DisposeAsync().AsTask().GetAwaiter().GetResult();
            dictationCoordinator = null;
        }
        if (pipeServer is not null)
        {
            pipeServer.DisposeAsync().AsTask().GetAwaiter().GetResult();
            pipeServer = null;
        }

        notifyIcon.Dispose();
        trayMenu.Dispose();
        activeIcon.Dispose();
        inactiveIcon.Dispose();
        listeningIcon.Dispose();
        dispatcher.Dispose();
        speechCommandGate.Dispose();
        lifetime.Dispose();
    }
}
