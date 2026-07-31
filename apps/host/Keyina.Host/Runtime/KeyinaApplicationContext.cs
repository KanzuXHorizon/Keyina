using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Keyina.Host.Configuration;
using Keyina.Host.Core;
using Keyina.Host.Core.Applications;
using Keyina.Host.Core.Configuration;
using Keyina.Host.Core.Feedback;
using Keyina.Host.Core.Hotkeys;
using Keyina.Host.Core.Ipc;
using Keyina.Host.Core.Snippets;
using Keyina.Host.Core.Speech;
using Keyina.Host.Core.Translation;
using Keyina.Host.Speech;
using Keyina.Host.Translation;
using Keyina.Host.UI;
using Keyina.Host.UI.Feedback;
using Keyina.Host.UI.Fluent;
using Keyina.Host.Windows.Applications;
using Keyina.Host.Windows.Audio;
using Keyina.Host.Windows.Credentials;
using Keyina.Host.Windows.Feedback;
using Keyina.Host.Windows.Hotkeys;
using Keyina.Host.Windows.Ipc;
using Keyina.Host.Windows.Startup;
using Keyina.Host.Windows.Typing;
using Keyina.Speechmatics;

namespace Keyina.Host.Runtime;

public sealed class KeyinaApplicationContext : ApplicationContext
{
    private const int PushToTalkHotkeyId = 1;
    private const int ToggleDictationHotkeyId = 2;
    private const int CancelDictationHotkeyId = 3;
    private const int TranslateSelectionHotkeyId = 4;
    private const int UndoTranslationHotkeyId = 5;
    private static readonly TimeSpan ActiveTextTargetReconnectGrace =
        TimeSpan.FromMilliseconds(750);

    private readonly KeyinaRuntimeOptions options;
    private readonly AtomicConfigurationStore configurationStore;
    private readonly RuntimeInputProfileStore runtimeInputProfileStore;
    private readonly RuntimeSnippetProfileStore runtimeSnippetProfileStore;
    private readonly WindowsStartupRegistration startupRegistration;
    private readonly ICredentialVault credentialVault;
    private readonly IApplicationExclusionService applicationExclusions;
    private readonly Keyina.Host.Windows.Networking.SafeEndpointValidator translationEndpointValidator = new();
    private readonly TsfSetupService tsfSetupService = new();
    private readonly HttpClient? translationHttpClient;
    private readonly HttpClient? libreTranslationHttpClient;
    private readonly TranslationCoordinator translationCoordinator;
    private readonly ConcurrentQueue<Action> pendingSignals = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim speechCommandGate = new(1, 1);
    private readonly object configurationSaveSync = new();
    private Task pendingConfigurationSave = Task.CompletedTask;
    private readonly Control dispatcher = new();
    private readonly ContextMenuStrip trayMenu = new();
    private readonly ToolStripMenuItem statusMenuItem;
    private readonly ToolStripMenuItem setupMenuItem;
    private readonly ToolStripMenuItem toggleVietnameseMenuItem;
    private readonly ToolStripMenuItem toggleDictationMenuItem;
    private readonly ToolStripMenuItem translateSelectionMenuItem;
    private readonly ToolStripMenuItem undoTranslationMenuItem;
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
    private ActivePipeEnvelopeWriter? textEnvelopeWriter;
    private FocusedUnicodeEnvelopeWriter? focusedDictationWriter;
    private DictationCoordinator? dictationCoordinator;
    private DictationOverlayModel? dictationOverlay;
    private DictationOverlayForm? dictationOverlayForm;
    private readonly SnippetSuggestionSession snippetSuggestionSession = new();
    private SnippetSuggestionOverlayForm? snippetSuggestionOverlayForm;
    private FeedbackCoordinator? feedbackCoordinator;
    private SettingsForm? settingsForm;
    private FirstRunForm? firstRunForm;
    private TranslationPreviewForm? translationPreviewForm;
    private int drainScheduled;
    private bool hotkeysReady;
    private bool translationHotkeyReady;
    private bool typingReady;
    private bool pipeReady;
    private bool endToEndTypingPassed;
    private DateTimeOffset? lastTypingTestAt;
    private string? lastExternalApplicationName;
    private FluentThemeMode? trayThemeMode;
    private DictationStatus? lastFeedbackDictationStatus;
    private bool runtimeInputProfileReady;
    private bool disposed;

    public KeyinaApplicationContext(KeyinaRuntimeOptions options)
        : this(options, null, null, null, null)
    {
    }

    public KeyinaApplicationContext(
        KeyinaRuntimeOptions options,
        ICredentialVault? credentialVault,
        ITranslationProvider? translationProvider,
        ISelectedTextAccessor? selectedTextAccessor,
        IApplicationExclusionService? applicationExclusions = null)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        options.Validate();
        this.credentialVault = credentialVault ?? new WindowsCredentialVault();
        this.applicationExclusions = applicationExclusions ??
            new ApplicationExclusionService();
        configurationStore = new AtomicConfigurationStore(options.ConfigurationPath);
        runtimeInputProfileStore = new RuntimeInputProfileStore(
            options.ResolveRuntimeInputProfilePath());
        runtimeSnippetProfileStore = new RuntimeSnippetProfileStore(
            options.ResolveRuntimeSnippetProfilePath());
        configuration = LoadConfiguration();
        this.applicationExclusions.Update(configuration.Applications);
        if (translationProvider is null)
        {
            translationHttpClient = CreateTranslationHttpClient();
            libreTranslationHttpClient = CreateTranslationHttpClient();
            var deepLProvider = new DeepLTranslationProvider(translationHttpClient);
            var libreProvider = new LibreTranslateProvider(
                libreTranslationHttpClient,
                translationEndpointValidator,
                () => configuration.TranslationProviders);
            translationProvider = new FallbackTranslationProvider(
                deepLProvider,
                libreProvider,
                () => configuration.TranslationProviders.LibreTranslateEnabled,
                ReadLibreTranslateApiKeySafely);
        }
        translationCoordinator = new TranslationCoordinator(
            selectedTextAccessor ?? new ClipboardSelectionAccessor(),
            translationProvider);
        state = HostState.Initial with
        {
            VietnameseEnabled = configuration.VietnameseEnabled,
        };
        if (options.PublishRuntimeProfileOnStartup)
        {
            PublishRuntimeInputProfileAtStartup();
        }
        startupRegistration = new WindowsStartupRegistration(
            options.StartupValueName,
            NativeResidentPathResolver.ResolveCurrentProcessSibling);

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
            ShortcutKeyDisplayString = FormatShortcutDisplay(
                configuration.Hotkeys.ToggleVietnamese.Chord),
            AccessibleName = "Bật hoặc tắt bộ gõ tiếng Việt",
        };
        toggleDictationMenuItem = new ToolStripMenuItem
        {
            Name = "toggleDictation",
            ShortcutKeyDisplayString = FormatShortcutDisplay(
                configuration.Hotkeys.ToggleDictation.Chord),
            AccessibleName = "Bắt đầu hoặc dừng nhập bằng giọng nói",
        };
        translateSelectionMenuItem = new ToolStripMenuItem
        {
            Name = "translateSelection",
            Text = "Dịch văn bản đang chọn",
            ShortcutKeyDisplayString = FormatShortcutDisplay(
                configuration.Hotkeys.TranslateSelection.Chord),
            AccessibleName = "Dịch văn bản đang chọn sang ngôn ngữ đã cài đặt",
        };
        undoTranslationMenuItem = new ToolStripMenuItem
        {
            Name = "undoTranslation",
            Text = "Hoàn tác bản dịch",
            ShortcutKeyDisplayString = FormatShortcutDisplay(
                configuration.Hotkeys.UndoTranslation.Chord),
            AccessibleName = "Khôi phục văn bản gốc của lần dịch gần nhất",
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
            translateSelectionMenuItem,
            undoTranslationMenuItem,
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
        translateSelectionMenuItem.Click += (_, _) =>
            PostCommand(HotkeyCommand.TranslateSelection);
        undoTranslationMenuItem.Click += (_, _) =>
            PostCommand(HotkeyCommand.UndoTranslation);
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
        if (configuration.FirstRunCompleted != true &&
            options.DisplaySettingsWindows)
        {
            OpenFirstRun();
        }
        else if (options.ShowSettingsOnStart)
        {
            OpenSettings();
        }
    }

    public HostState CurrentState => state;

    public SettingsSnapshot CurrentSettingsSnapshot => CreateSettingsSnapshot();

    public bool SettingsCreated => settingsForm is not null && !settingsForm.IsDisposed;

    public bool FirstRunCreated => firstRunForm is not null && !firstRunForm.IsDisposed;

    public bool NotifyIconVisible => notifyIcon.Visible;

    public bool RuntimeInputProfileReady => runtimeInputProfileReady;

    public bool FocusedDictationReady =>
        focusedDictationWriter is not null && dictationCoordinator is not null;

    public bool CanUndoTranslation => translationCoordinator.CanUndo;

    public bool TranslationPreviewCreated =>
        translationPreviewForm is not null && !translationPreviewForm.IsDisposed;

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
            case HotkeyCommand.TranslateSelection:
                await TranslateSelectionAsync(cancellationToken).ConfigureAwait(true);
                return;
            case HotkeyCommand.UndoTranslation:
                await UndoTranslationAsync(cancellationToken).ConfigureAwait(true);
                return;
            case HotkeyCommand.CancelDictation:
                translationCoordinator.Cancel();
                await CancelDictationAsync().ConfigureAwait(true);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }
    }

    public void OpenSettings(string? section = null) =>
        OpenSettingsCore(section, forceDisplay: false);

    private void OpenCredentialSetup(CredentialSetupTarget target)
    {
        var section = target switch
        {
            CredentialSetupTarget.Speechmatics => "speech",
            CredentialSetupTarget.Translation or
            CredentialSetupTarget.LibreTranslate => "translation",
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
        OpenSettingsCore(section, forceDisplay: true);
        settingsForm!.OpenCredentialSetup(target);
        settingsForm.Activate();
    }

    private void OpenSettingsCore(string? section, bool forceDisplay)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        RememberForegroundApplication();
        if (SettingsCreated)
        {
            if (settingsForm!.WindowState == FormWindowState.Minimized)
            {
                settingsForm.WindowState = FormWindowState.Normal;
            }
            if (options.DisplaySettingsWindows || forceDisplay)
            {
                settingsForm.Show();
            }
            if (!string.IsNullOrWhiteSpace(section))
            {
                settingsForm.OpenSection(section);
            }
            settingsForm.Activate();
            return;
        }

        settingsForm = new SettingsForm(CreateSettingsSnapshot(), CreateSettingsActions());
        settingsForm.FormClosed += (_, _) =>
        {
            settingsForm = null;
            ScheduleCompanionExitIfIdle();
        };
        if (!string.IsNullOrWhiteSpace(section))
        {
            settingsForm.OpenSection(section);
        }
        if (options.DisplaySettingsWindows || forceDisplay)
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

    private void RememberForegroundApplication()
    {
        try
        {
            var executableName = applicationExclusions.GetForegroundExecutableName();
            var ownExecutableName = Path.GetFileName(Environment.ProcessPath);
            if (!string.IsNullOrWhiteSpace(executableName) &&
                !string.Equals(
                    executableName,
                    ownExecutableName,
                    StringComparison.OrdinalIgnoreCase))
            {
                lastExternalApplicationName = executableName;
            }
        }
        catch (Exception)
        {
            // Current-application assistance is optional.
        }
    }

    private void OpenFirstRun()
    {
        RememberForegroundApplication();
        if (FirstRunCreated)
        {
            firstRunForm!.Show();
            firstRunForm.Activate();
            return;
        }

        firstRunForm = new FirstRunForm(
            section =>
            {
                CompleteFirstRun();
                firstRunForm?.Close();
                OpenSettings(section);
            },
            CompleteFirstRun);
        firstRunForm.FormClosed += (_, _) =>
        {
            if (!disposed && configuration.FirstRunCompleted != true)
            {
                CompleteFirstRun();
            }
            firstRunForm = null;
            ScheduleCompanionExitIfIdle();
        };
        firstRunForm.Show();
    }

    private void CompleteFirstRun()
    {
        if (configuration.FirstRunCompleted == true)
        {
            return;
        }

        configuration = configuration with { FirstRunCompleted = true };
        _ = SaveConfigurationSafelyAsync();
        RefreshVisualState();
    }

    private void ScheduleCompanionExitIfIdle()
    {
        if (!options.ExitWhenLastWindowCloses || disposed || dispatcher.IsDisposed)
        {
            return;
        }

        dispatcher.BeginInvoke(() =>
        {
            if (!disposed &&
                options.ExitWhenLastWindowCloses &&
                !SettingsCreated &&
                !FirstRunCreated)
            {
                ExitThread();
            }
        });
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

    public bool IsDictationActive => dictationCoordinator?.State.Status is
        DictationStatus.Connecting or
        DictationStatus.Listening or
        DictationStatus.Finalizing;

    private static HttpClient CreateTranslationHttpClient() => new(
        new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression =
                System.Net.DecompressionMethods.GZip |
                System.Net.DecompressionMethods.Deflate,
            ConnectTimeout = TimeSpan.FromSeconds(5),
        },
        disposeHandler: true);

    private string? ReadLibreTranslateApiKeySafely()
    {
        try
        {
            return credentialVault.Read(CredentialTargets.LibreTranslateApiKey);
        }
        catch (Exception)
        {
            return null;
        }
    }

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
                hotkeyWindow.Invoke(() =>
                {
                    hotkeyManager.Register(
                        CreateRequiredRegisteredBindings(configuration.Hotkeys));
                    translationHotkeyReady = configuration.TranslationEnabled &&
                        IsTranslationProviderConfigured() &&
                        hotkeyManager.TryRegister(
                            CreateTranslationRegisteredBinding(configuration.Hotkeys),
                            out _);
                });
                hotkeysReady = true;
            }
            catch (Exception)
            {
                hotkeyManager?.Dispose();
                hotkeyManager = null;
                hotkeyWindow?.Dispose();
                hotkeyWindow = null;
                hotkeysReady = false;
                translationHotkeyReady = false;
                ReportFailure("hotkey_start_failed");
            }

            try
            {
                typingHook = new VietnameseKeyboardHook(
                    shouldBypassApplication: processId =>
                        this.applicationExclusions.IsDisabled(
                            ApplicationFeature.VietnameseTyping,
                            processId));
                modifierHook = new ModifierKeyboardHook(
                    new SharedTypingKeyboardHookNativeApi(typingHook));
                modifierHook.Configure(
                    configuration.Hotkeys.ToggleVietnamese,
                    configuration.Hotkeys.PushToTalk);
                modifierHook.CommandReceived += (_, command) => PostCommand(command);
                modifierHook.RawEventReceived += rawEvent =>
                    PostSignal(() => HandleSnippetRawEvent(rawEvent));
                UpdateSnippetSuggestions();
                modifierHook.Start();
                typingHook.Start(configuration.VietnameseEnabled);
                typingReady = true;
            }
            catch (Exception)
            {
                modifierHook?.Dispose();
                modifierHook = null;
                typingHook?.Dispose();
                typingHook = null;
                typingReady = false;
                ReportFailure("typing_hook_start_failed");
            }
        }

        if (options.EnableSpeech)
        {
            IIpcEnvelopeWriter? envelopeWriter = null;
            Func<ulong>? focusGenerationProvider = null;
            if (pipeServer is not null)
            {
                var activePipeWriter = new ActivePipeEnvelopeWriter(pipeServer);
                textEnvelopeWriter = activePipeWriter;
                envelopeWriter = activePipeWriter;
                focusGenerationProvider = () =>
                    activePipeWriter.CurrentFocusGeneration;
            }
            else if (options.FocusedDictationWriterFactory is not null)
            {
                var focusedWriter = options.FocusedDictationWriterFactory.Invoke();
                focusedDictationWriter = focusedWriter;
                envelopeWriter = focusedWriter;
                focusGenerationProvider = () => focusedWriter.FocusGeneration;
            }

            if (envelopeWriter is not null && focusGenerationProvider is not null)
            {
                dictationOverlay = new DictationOverlayModel();
                dictationOverlay.StateChanged += (_, _) =>
                    PostSignal(HandleDictationStateChanged);
                dictationCoordinator = new DictationCoordinator(
                    new SpeechmaticsSessionFactory(),
                    new WasapiMicrophoneCapture(),
                    envelopeWriter,
                    dictationOverlay,
                    hostEvent => PostSignal(() => ApplyHostEvent(hostEvent)),
                    focusGenerationProvider,
                    TimeSpan.FromSeconds(5));
            }
        }
    }

    private static RegisteredHotkeyBinding[] CreateRequiredRegisteredBindings(
        HotkeyPreferences preferences) =>
    [
        new(
            PushToTalkHotkeyId,
            preferences.PushToTalk.Chord,
            HotkeyCommand.PushToTalkPressed),
        new(
            ToggleDictationHotkeyId,
            preferences.ToggleDictation.Chord,
            HotkeyCommand.ToggleDictation),
        new(
            UndoTranslationHotkeyId,
            preferences.UndoTranslation.Chord,
            HotkeyCommand.UndoTranslation),
    ];

    private static RegisteredHotkeyBinding CreateTranslationRegisteredBinding(
        HotkeyPreferences preferences) =>
        new(
            TranslateSelectionHotkeyId,
            preferences.TranslateSelection.Chord,
            HotkeyCommand.TranslateSelection);

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

    public Task ApplyNativeVietnameseStateAsync(
        bool enabled,
        CancellationToken cancellationToken) =>
        SetVietnameseEnabledAsync(enabled, cancellationToken);

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

    private void SetTranslationEnabled(bool enabled)
    {
        configuration = configuration with { TranslationEnabled = enabled };
        UpdateTranslationHotkeyRegistration(
            enabled && IsTranslationProviderConfigured());
        _ = SaveConfigurationSafelyAsync();
        RefreshVisualState();
    }

    private bool IsTranslationProviderConfigured() =>
        IsDeepLCredentialConfigured() ||
        configuration.TranslationProviders.LibreTranslateEnabled;

    private bool IsDeepLCredentialConfigured()
    {
        try
        {
            return !string.IsNullOrWhiteSpace(
                credentialVault.Read(CredentialTargets.DeepLApiKey));
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void UpdateTranslationHotkeyRegistration(bool enabled)
    {
        if (!options.EnableGlobalHotkeys || hotkeyWindow is null || hotkeyManager is null)
        {
            translationHotkeyReady = false;
            return;
        }

        try
        {
            var wasRegistered = translationHotkeyReady;
            translationHotkeyReady = hotkeyWindow.Invoke(() =>
            {
                if (enabled)
                {
                    return wasRegistered || hotkeyManager.TryRegister(
                        CreateTranslationRegisteredBinding(configuration.Hotkeys),
                        out _);
                }
                if (!wasRegistered)
                {
                    return false;
                }

                return !hotkeyManager.TryUnregister(
                    TranslateSelectionHotkeyId,
                    out _);
            });
        }
        catch (Exception)
        {
            if (enabled)
            {
                translationHotkeyReady = false;
            }
        }
    }

    private void SetTranslationPreviewEnabled(bool enabled)
    {
        configuration = configuration with { TranslationPreviewEnabled = enabled };
        if (!enabled)
        {
            CloseTranslationPreview();
        }
        _ = SaveConfigurationSafelyAsync();
        RefreshVisualState();
    }

    private void SetTranslationProviders(TranslationProviderPreferences preferences)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(preferences);
            var normalized = preferences.Normalize();
            if (normalized.LibreTranslateEnabled)
            {
                _ = translationEndpointValidator.ValidateTranslateEndpointAsync(
                        normalized.LibreTranslateEndpoint,
                        normalized.AllowLocalEndpoint,
                        CancellationToken.None)
                    .GetAwaiter().GetResult();
            }

            configuration = configuration with
            {
                TranslationProviders = normalized,
            };
            UpdateTranslationHotkeyRegistration(
                configuration.TranslationEnabled &&
                IsTranslationProviderConfigured());
            _ = SaveConfigurationSafelyAsync();
            RecoverHost();
            PublishFeedback(FeedbackEvents.Success(
                normalized.LibreTranslateEnabled
                    ? "Đã bật fallback LibreTranslate"
                    : "Đã tắt fallback LibreTranslate"));
        }
        catch (ArgumentException)
        {
            ReportFailure("translation_provider_endpoint_invalid");
            PublishFeedback(FeedbackEvents.Error(
                "Endpoint LibreTranslate không an toàn hoặc không thể phân giải"));
        }
        catch (Exception)
        {
            ReportFailure("translation_provider_update_failed");
            PublishFeedback(FeedbackEvents.Error(
                "Không thể cập nhật provider dịch"));
        }
        RefreshVisualState();
    }

    private void SetTranslationTargetLanguage(string targetLanguage)
    {
        try
        {
            configuration = configuration with
            {
                TranslationTargetLanguage =
                    TranslationLanguageCatalog.NormalizeTarget(targetLanguage),
            };
            _ = SaveConfigurationSafelyAsync();
            RecoverHost();
        }
        catch (Exception)
        {
            ReportFailure("translation_target_invalid");
        }
        RefreshVisualState();
    }

    private void SetHotkey(HotkeyCommand command, HotkeyChord chord)
    {
        try
        {
            if (IsDictationActive)
            {
                ReportFailure("hotkey_update_while_dictating");
                PublishFeedback(FeedbackEvents.Error("Hãy dừng nhập giọng nói trước khi đổi phím tắt"));
                return;
            }

            var candidate = configuration.Hotkeys.WithChord(command, chord);
            candidate.Validate();
            if (!TryApplyHotkeyPreferences(candidate, command))
            {
                ReportFailure("hotkey_registration_conflict");
                PublishFeedback(FeedbackEvents.Error("Phím tắt đang được ứng dụng khác sử dụng"));
                return;
            }

            configuration = configuration with { Hotkeys = candidate };
            _ = SaveConfigurationSafelyAsync();
            RecoverHost();
        }
        catch (ArgumentException)
        {
            ReportFailure("hotkey_invalid");
            PublishFeedback(FeedbackEvents.Error("Tổ hợp phím không hợp lệ hoặc bị trùng"));
        }
        catch (Exception)
        {
            ReportFailure("hotkey_update_failed");
            PublishFeedback(FeedbackEvents.Error("Không thể cập nhật phím tắt"));
        }
        finally
        {
            RefreshVisualState();
        }
    }

    private void ResetHotkey(HotkeyCommand command)
    {
        var defaultBinding = HotkeyPreferences.Default.ToBindings()
            .Single(binding => binding.Command == command);
        SetHotkey(command, defaultBinding.Chord);
    }

    private void ResetAllHotkeys()
    {
        try
        {
            if (!TryApplyHotkeyPreferences(
                    HotkeyPreferences.Default,
                    changedCommand: null))
            {
                ReportFailure("hotkey_registration_conflict");
                PublishFeedback(FeedbackEvents.Error("Phím tắt mặc định đang bị ứng dụng khác sử dụng"));
                return;
            }

            configuration = configuration with { Hotkeys = HotkeyPreferences.Default };
            _ = SaveConfigurationSafelyAsync();
            RecoverHost();
        }
        catch (Exception)
        {
            ReportFailure("hotkey_reset_failed");
            PublishFeedback(FeedbackEvents.Error("Không thể khôi phục phím tắt mặc định"));
        }
        finally
        {
            RefreshVisualState();
        }
    }

    private bool TryApplyHotkeyPreferences(
        HotkeyPreferences candidate,
        HotkeyCommand? changedCommand)
    {
        candidate.Validate();
        if (!options.EnableGlobalHotkeys ||
            hotkeyWindow is null ||
            hotkeyManager is null)
        {
            modifierHook?.Configure(
                candidate.ToggleVietnamese,
                candidate.PushToTalk);
            return true;
        }

        var translationEnabled = configuration.TranslationEnabled &&
            IsTranslationProviderConfigured();
        var translationChanged =
            candidate.TranslateSelection.Chord !=
            configuration.Hotkeys.TranslateSelection.Chord;
        var requireTranslationRegistration = translationEnabled &&
            (translationHotkeyReady ||
             translationChanged ||
             changedCommand == HotkeyCommand.TranslateSelection);
        var requiredBindings = CreateRequiredRegisteredBindings(candidate);
        var replacement = requireTranslationRegistration
            ? requiredBindings
                .Append(CreateTranslationRegisteredBinding(candidate))
                .ToArray()
            : requiredBindings;

        HotkeyRegistrationException? failure = null;
        var replaced = hotkeyWindow.Invoke(() =>
            hotkeyManager.TryReplaceAll(replacement, out failure));
        if (!replaced)
        {
            return false;
        }

        modifierHook?.Configure(
            candidate.ToggleVietnamese,
            candidate.PushToTalk);
        translationHotkeyReady = requireTranslationRegistration;
        if (translationEnabled && !translationHotkeyReady)
        {
            translationHotkeyReady = hotkeyWindow.Invoke(() =>
                hotkeyManager.TryRegister(
                    CreateTranslationRegisteredBinding(candidate),
                    out _));
        }
        hotkeysReady = true;
        return true;
    }

    private void SetApplicationPreferences(ApplicationPreferences preferences)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(preferences);
            var normalized = preferences.Normalize();
            applicationExclusions.Update(normalized);
            configuration = configuration with { Applications = normalized };
            typingHook?.Reset();
            _ = SaveConfigurationSafelyAsync();
            RecoverHost();
            PublishFeedback(FeedbackEvents.Success("Đã cập nhật quy tắc ứng dụng"));
        }
        catch (ArgumentException)
        {
            ReportFailure("application_preferences_invalid");
            PublishFeedback(FeedbackEvents.Error(
                "Tên ứng dụng không hợp lệ; chỉ nhập tên file .exe"));
        }
        catch (Exception)
        {
            ReportFailure("application_preferences_update_failed");
            PublishFeedback(FeedbackEvents.Error(
                "Không thể cập nhật quy tắc ứng dụng"));
        }
        RefreshVisualState();
    }

    private void SetSnippets(IReadOnlyList<SnippetConfiguration> snippets)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(snippets);
            var candidate = snippets.ToArray();
            var updated = configuration with { Snippets = candidate };
            _ = updated.ValidateAndCreateSnippets();
            configuration = updated;
            UpdateSnippetSuggestions();
            _ = SaveConfigurationSafelyAsync();
            RecoverHost();
            PublishFeedback(FeedbackEvents.Success("Đã cập nhật gõ tắt"));
        }
        catch (ConfigurationValidationException)
        {
            ReportFailure("snippet_configuration_invalid");
            PublishFeedback(FeedbackEvents.Error("Gõ tắt không hợp lệ hoặc bị trùng"));
        }
        catch (Exception)
        {
            ReportFailure("snippet_configuration_update_failed");
            PublishFeedback(FeedbackEvents.Error("Không thể cập nhật gõ tắt"));
        }
        RefreshVisualState();
    }

    private void UpdateSnippetSuggestions()
    {
        snippetSuggestionSession.UpdateDefinitions(
            BuiltInSnippets.Create().Concat(configuration.ValidateAndCreateSnippets()));
    }

    private void HandleSnippetRawEvent(RawKeyboardEvent rawEvent)
    {
        if (!rawEvent.IsKeyDown || rawEvent.IsInjected ||
            applicationExclusions.IsForegroundDisabled(ApplicationFeature.VietnameseTyping))
        {
            return;
        }

        var character = RawKeyToSnippetCharacter(rawEvent.Key);
        if (character is null)
        {
            if (!rawEvent.Key.IsModifier())
            {
                snippetSuggestionSession.Reset();
                snippetSuggestionOverlayForm?.HideOverlay();
            }
            return;
        }

        var suggestions = snippetSuggestionSession.Push(character.Value);
        if (suggestions.Count == 0)
        {
            snippetSuggestionOverlayForm?.HideOverlay();
            return;
        }

        snippetSuggestionOverlayForm ??= new SnippetSuggestionOverlayForm();
        snippetSuggestionOverlayForm.Present(snippetSuggestionSession.Prefix, suggestions);
    }

    private static char? RawKeyToSnippetCharacter(VirtualKey key) => key switch
    {
        VirtualKey.Semicolon => ';',
        VirtualKey.Backspace => '\b',
        VirtualKey.Escape => '\u001b',
        VirtualKey.Space or VirtualKey.Enter or VirtualKey.Tab => ' ',
        >= VirtualKey.A and <= VirtualKey.Z =>
            (char)('a' + ((int)key - (int)VirtualKey.A)),
        >= VirtualKey.D0 and <= VirtualKey.D9 =>
            (char)('0' + ((int)key - (int)VirtualKey.D0)),
        _ => null,
    };

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
            if (applicationExclusions.IsForegroundDisabled(ApplicationFeature.Speech))
            {
                ReportFailure("speech_application_excluded");
                PublishFeedback(FeedbackEvents.Error(
                    "Nhập giọng nói đã tắt cho ứng dụng này"));
                return;
            }
            IpcSessionId targetSessionId;
            if (focusedDictationWriter is not null)
            {
                if (!focusedDictationWriter.TryBindToCurrentFocus())
                {
                    ReportFailure("speech_no_focused_app");
                    PublishFeedback(FeedbackEvents.Error(
                        "Chưa có ô nhập văn bản an toàn"));
                    return;
                }
                targetSessionId = focusedDictationWriter.SessionId;
            }
            else
            {
                var target = textEnvelopeWriter is null
                    ? null
                    : await textEnvelopeWriter.CaptureTargetAsync(
                            ActiveTextTargetReconnectGrace,
                            cancellationToken)
                        .ConfigureAwait(true);
                if (target is null)
                {
                    ReportFailure("speech_no_focused_app");
                    PublishFeedback(FeedbackEvents.Error(
                        "Chưa có ô nhập văn bản đang hoạt động"));
                    return;
                }
                targetSessionId = target.SessionId;
            }
            var apiKey = credentialVault.Read(CredentialTargets.SpeechmaticsApiKey);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                ReportFailure("speech_credential_missing");
                PublishFeedback(FeedbackEvents.Error(
                    "Nhập khóa Speechmatics trong cửa sổ Cài đặt vừa mở"));
                OpenCredentialSetup(CredentialSetupTarget.Speechmatics);
                return;
            }

            RecoverHost();
            await dictationCoordinator.StartAsync(
                    apiKey,
                    targetSessionId,
                    cancellationToken)
                .ConfigureAwait(true);
        }
        catch (SpeechmaticsSessionException exception) when (
            exception.IsAuthenticationFailure)
        {
            ReportFailure("speech_credential_invalid");
            PublishFeedback(FeedbackEvents.Error(
                "Khóa Speechmatics không hợp lệ; hãy cập nhật trong Cài đặt vừa mở"));
            OpenCredentialSetup(CredentialSetupTarget.Speechmatics);
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

    private async Task TranslateSelectionAsync(CancellationToken cancellationToken)
    {
        CloseTranslationPreview();
        if (!configuration.TranslationEnabled)
        {
            ReportFailure("translation_disabled");
            PublishFeedback(FeedbackEvents.Error("Dịch nhanh đang tắt"));
            return;
        }
        if (applicationExclusions.IsForegroundDisabled(ApplicationFeature.Translation))
        {
            ReportFailure("translation_application_excluded");
            PublishFeedback(FeedbackEvents.Error(
                "Dịch nhanh đã tắt cho ứng dụng này"));
            return;
        }

        string? apiKey;
        try
        {
            apiKey = credentialVault.Read(CredentialTargets.DeepLApiKey);
        }
        catch (Exception)
        {
            apiKey = null;
        }
        if (string.IsNullOrWhiteSpace(apiKey) &&
            !configuration.TranslationProviders.LibreTranslateEnabled)
        {
            ReportFailure("translation_provider_missing");
            PublishFeedback(FeedbackEvents.Error(
                "Nhập khóa DeepL hoặc bật LibreTranslate trong Cài đặt vừa mở"));
            OpenCredentialSetup(CredentialSetupTarget.Translation);
            return;
        }

        var targetDisplayName = TranslationLanguageCatalog.GetDisplayName(
            configuration.TranslationTargetLanguage);
        PublishFeedback(FeedbackEvents.TranslationStarted(targetDisplayName));
        var outcome = await translationCoordinator.TranslateSelectionAsync(
                apiKey ?? string.Empty,
                configuration.TranslationTargetLanguage,
                cancellationToken,
                preview: configuration.TranslationPreviewEnabled)
            .ConfigureAwait(true);
        if (outcome.Status == TranslationOutcomeStatus.PreviewReady &&
            outcome.Preview is { } preview)
        {
            OpenTranslationPreview(preview);
            RecoverHost();
            PublishFeedback(FeedbackEvents.Success("Bản dịch đã sẵn sàng để xem trước"));
            RefreshVisualState();
            return;
        }
        if (outcome.Status == TranslationOutcomeStatus.Cancelled)
        {
            PublishFeedback(FeedbackEvents.TranslationCancelled());
            return;
        }
        if (outcome.Status == TranslationOutcomeStatus.Succeeded)
        {
            RecoverHost();
            PublishFeedback(FeedbackEvents.TranslationCompleted(targetDisplayName));
            RefreshVisualState();
            return;
        }

        var (errorCode, message) = MapTranslationFailure(outcome.FailureCode);
        ReportFailure(errorCode);
        PublishFeedback(FeedbackEvents.Error(message));
        if (outcome.FailureCode == TranslationFailureCode.AuthenticationFailed)
        {
            OpenCredentialSetup(
                string.IsNullOrWhiteSpace(apiKey) &&
                configuration.TranslationProviders.LibreTranslateEnabled
                    ? CredentialSetupTarget.LibreTranslate
                    : CredentialSetupTarget.Translation);
        }
    }

    private void OpenTranslationPreview(TranslationPreview preview)
    {
        CloseTranslationPreview();
        translationPreviewForm = new TranslationPreviewForm(
            preview,
            ApplyTranslationPreview,
            CopyTranslationPreview,
            () => PublishFeedback(FeedbackEvents.TranslationCancelled()));
        translationPreviewForm.FormClosed += (_, _) => translationPreviewForm = null;
        if (options.DisplayTranslationPreview)
        {
            translationPreviewForm.Show();
            translationPreviewForm.Activate();
        }
    }

    private void ApplyTranslationPreview(TranslationPreview preview)
    {
        var outcome = translationCoordinator.ApplyPreview(preview);
        if (outcome.Status == TranslationOutcomeStatus.Succeeded)
        {
            RecoverHost();
            PublishFeedback(FeedbackEvents.Success("Đã thay thế bằng bản dịch"));
            RefreshVisualState();
            return;
        }

        var (errorCode, message) = MapTranslationFailure(outcome.FailureCode);
        ReportFailure(errorCode);
        PublishFeedback(FeedbackEvents.Error(message));
        RefreshVisualState();
    }

    private void CopyTranslationPreview(string translatedText)
    {
        try
        {
            Clipboard.SetText(translatedText, TextDataFormat.UnicodeText);
            RecoverHost();
            PublishFeedback(FeedbackEvents.Success("Đã sao chép bản dịch"));
        }
        catch (ExternalException)
        {
            ReportFailure("translation_preview_copy_failed");
            PublishFeedback(FeedbackEvents.Error("Clipboard đang bận; hãy thử sao chép lại"));
        }
        RefreshVisualState();
    }

    private void CloseTranslationPreview()
    {
        var form = translationPreviewForm;
        translationPreviewForm = null;
        if (form is null)
        {
            return;
        }
        try
        {
            form.Close();
            form.Dispose();
        }
        catch (Exception)
        {
            // Preview cleanup is optional and must not affect the resident host.
        }
    }

    private async Task UndoTranslationAsync(CancellationToken cancellationToken)
    {
        try
        {
            var restored = await translationCoordinator.UndoAsync(cancellationToken)
                .ConfigureAwait(true);
            if (!restored)
            {
                ReportFailure("translation_undo_unavailable");
                PublishFeedback(FeedbackEvents.Error(
                    "Không còn bản dịch hợp lệ để hoàn tác"));
                return;
            }

            RecoverHost();
            PublishFeedback(FeedbackEvents.Success("Đã khôi phục văn bản gốc"));
        }
        catch (OperationCanceledException)
        {
            PublishFeedback(FeedbackEvents.TranslationCancelled());
        }
        catch (Exception)
        {
            ReportFailure("translation_undo_failed");
            PublishFeedback(FeedbackEvents.Error(
                "Không thể hoàn tác vì vị trí nhập đã thay đổi"));
        }
        finally
        {
            RefreshVisualState();
        }
    }

    private static (string ErrorCode, string Message) MapTranslationFailure(
        TranslationFailureCode? failureCode) => failureCode switch
        {
            TranslationFailureCode.NoSelection => (
                "translation_no_selection",
                "Chưa chọn văn bản để dịch"),
            TranslationFailureCode.FocusChanged => (
                "translation_focus_changed",
                "Đã đổi vị trí nhập, không chèn bản dịch"),
            TranslationFailureCode.PreviewExpired => (
                "translation_preview_expired",
                "Bản xem trước đã hết hạn; hãy dịch lại"),
            TranslationFailureCode.AuthenticationFailed => (
                "translation_auth_failed",
                "Khóa dịch không hợp lệ; hãy cập nhật trong Cài đặt vừa mở"),
            TranslationFailureCode.RateLimited => (
                "translation_rate_limited",
                "Dịch vụ dịch đang giới hạn yêu cầu"),
            TranslationFailureCode.QuotaExceeded => (
                "translation_quota_exceeded",
                "Đã hết hạn mức của dịch vụ dịch"),
            TranslationFailureCode.InvalidResponse => (
                "translation_invalid_response",
                "Bản dịch không giữ nguyên nội dung kỹ thuật"),
            TranslationFailureCode.UnsupportedLanguage => (
                "translation_unsupported_language",
                "Ngôn ngữ đích không được hỗ trợ"),
            TranslationFailureCode.SelectionTooLarge => (
                "translation_selection_too_large",
                "Đoạn chọn quá dài để dịch"),
            _ => (
                "translation_unavailable",
                "Không thể kết nối dịch vụ dịch"),
        };

    private void ApplyHostEvent(HostEvent hostEvent)
    {
        state = HostReducer.Reduce(state, hostEvent);
        RefreshVisualState();
        if (hostEvent is HostFailed
            {
                ErrorCode: "speech_credential_invalid",
            })
        {
            PublishFeedback(FeedbackEvents.Error(
                "Khóa Speechmatics không hợp lệ; hãy cập nhật trong Cài đặt vừa mở"));
            OpenCredentialSetup(CredentialSetupTarget.Speechmatics);
        }
    }

    private void HandleDictationStateChanged()
    {
        RefreshVisualState();
        if (dictationOverlay is null)
        {
            return;
        }

        var dictationState = dictationOverlay.State;
        var status = dictationState.Status;
        if (options.DisplayDictationOverlay)
        {
            if (status is DictationStatus.Connecting or
                DictationStatus.Listening or
                DictationStatus.Finalizing)
            {
                dictationOverlayForm ??= new DictationOverlayForm();
                dictationOverlayForm.Present(dictationState);
            }
            else
            {
                dictationOverlayForm?.HideOverlay();
            }
        }

        if (lastFeedbackDictationStatus == status)
        {
            return;
        }

        lastFeedbackDictationStatus = status;
        var feedbackEvent = FeedbackEvents.ForDictation(status);
        if (feedbackEvent is not null)
        {
            var dedicatedOverlayVisible = options.DisplayDictationOverlay &&
                status is DictationStatus.Connecting or
                    DictationStatus.Listening or
                    DictationStatus.Finalizing;
            PublishFeedback(feedbackEvent, dedicatedOverlayVisible);
        }
    }

    private void PublishFeedback(
        FeedbackEvent feedbackEvent,
        bool suppressVisual = false)
    {
        try
        {
            feedbackCoordinator?.Publish(
                feedbackEvent,
                suppressVisual: suppressVisual ||
                    applicationExclusions.IsForegroundDisabled(
                        ApplicationFeature.VisualFeedback));
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

    private void ExportPortableSettings(string path)
    {
        try
        {
            PortableSettingsService.ExportAsync(
                    path,
                    configuration,
                    CancellationToken.None)
                .GetAwaiter().GetResult();
            RecoverHost();
            PublishFeedback(FeedbackEvents.Success("Đã xuất cài đặt"));
        }
        catch (Exception)
        {
            ReportFailure("settings_export_failed");
            PublishFeedback(FeedbackEvents.Error("Không thể xuất cài đặt"));
        }
        RefreshVisualState();
    }

    private void ImportPortableSettings(string path)
    {
        var previous = configuration;
        try
        {
            var candidate = PortableSettingsService.ImportAsync(
                    path,
                    CancellationToken.None)
                .GetAwaiter().GetResult() with
            {
                FirstRunCompleted = true,
            };
            _ = candidate.ValidateAndCreateSnippets();
            if (!TryApplyHotkeyPreferences(candidate.Hotkeys, changedCommand: null))
            {
                ReportFailure("settings_import_hotkey_conflict");
                PublishFeedback(FeedbackEvents.Error(
                    "Không thể nhập vì phím tắt đang bị ứng dụng khác sử dụng"));
                return;
            }

            configuration = candidate;
            ApplyImportedConfigurationToRuntime();
            SaveConfigurationAsync(CancellationToken.None)
                .GetAwaiter().GetResult();
            RecoverHost();
            PublishFeedback(FeedbackEvents.Success("Đã nhập cài đặt"));
        }
        catch (Exception)
        {
            configuration = previous;
            try
            {
                _ = TryApplyHotkeyPreferences(previous.Hotkeys, changedCommand: null);
                ApplyImportedConfigurationToRuntime();
            }
            catch (Exception)
            {
                // Preserve the original failure code; optional features remain fail-open.
            }
            ReportFailure("settings_import_failed");
            PublishFeedback(FeedbackEvents.Error(
                "File cài đặt không hợp lệ hoặc không thể áp dụng an toàn"));
        }
        finally
        {
            RefreshVisualState();
        }
    }

    private void ApplyImportedConfigurationToRuntime()
    {
        state = HostReducer.Reduce(
            state,
            new InputModeChanged(configuration.VietnameseEnabled));
        typingHook?.SetEnabled(configuration.VietnameseEnabled);
        applicationExclusions.Update(configuration.Applications);
        modifierHook?.Configure(
            configuration.Hotkeys.ToggleVietnamese,
            configuration.Hotkeys.PushToTalk);
        try
        {
            feedbackCoordinator?.UpdatePreferences(
                configuration.Feedback ?? FeedbackPreferences.Default);
        }
        catch (Exception)
        {
            // Feedback is optional and never blocks importing typing preferences.
        }
        UpdateTranslationHotkeyRegistration(
            configuration.TranslationEnabled && IsTranslationProviderConfigured());
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
        PreviewFeedback)
    {
        SetTranslationEnabled = SetTranslationEnabled,
        SetTranslationTargetLanguage = SetTranslationTargetLanguage,
        SetTranslationPreviewEnabled = SetTranslationPreviewEnabled,
        SetTranslationProviders = SetTranslationProviders,
        SaveDeepLApiKey = secret =>
        {
            try
            {
                credentialVault.Write(
                    CredentialTargets.DeepLApiKey,
                    secret.Trim());
                UpdateTranslationHotkeyRegistration(
                    configuration.TranslationEnabled);
                RecoverHost();
            }
            catch (Exception)
            {
                ReportFailure("translation_credential_write_failed");
            }
            RefreshVisualState();
        },
        DeleteDeepLApiKey = () =>
        {
            try
            {
                _ = credentialVault.Delete(CredentialTargets.DeepLApiKey);
                if (!configuration.TranslationProviders.LibreTranslateEnabled)
                {
                    configuration = configuration with { TranslationEnabled = false };
                    UpdateTranslationHotkeyRegistration(enabled: false);
                }
                _ = SaveConfigurationSafelyAsync();
                RecoverHost();
            }
            catch (Exception)
            {
                ReportFailure("translation_credential_delete_failed");
            }
            RefreshVisualState();
        },
        SaveLibreTranslateApiKey = secret =>
        {
            try
            {
                credentialVault.Write(
                    CredentialTargets.LibreTranslateApiKey,
                    secret);
                RecoverHost();
            }
            catch (Exception)
            {
                ReportFailure("libretranslate_credential_write_failed");
            }
            RefreshVisualState();
        },
        DeleteLibreTranslateApiKey = () =>
        {
            try
            {
                _ = credentialVault.Delete(
                    CredentialTargets.LibreTranslateApiKey);
                RecoverHost();
            }
            catch (Exception)
            {
                ReportFailure("libretranslate_credential_delete_failed");
            }
            RefreshVisualState();
        },
        SetHotkey = SetHotkey,
        ResetHotkey = ResetHotkey,
        ResetAllHotkeys = ResetAllHotkeys,
        ExportSettings = ExportPortableSettings,
        ImportSettings = ImportPortableSettings,
        SetApplicationPreferences = SetApplicationPreferences,
        SetSnippets = SetSnippets,
        GetForegroundApplicationName = () =>
            lastExternalApplicationName ??
            applicationExclusions.GetForegroundExecutableName(),
    };

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
        bool translationCredentialConfigured;
        bool libreTranslateCredentialConfigured;
        try
        {
            credentialConfigured = !string.IsNullOrWhiteSpace(
                credentialVault.Read(CredentialTargets.SpeechmaticsApiKey));
        }
        catch (Exception)
        {
            credentialConfigured = false;
        }
        translationCredentialConfigured = IsDeepLCredentialConfigured();
        libreTranslateCredentialConfigured = !string.IsNullOrWhiteSpace(
            ReadLibreTranslateApiKeySafely());

        var nativeResidentRunning = NativeResidentLauncher.IsRunning();
        var companionMode = options.UseNativeResidentHealth;
        var ipcConnected = companionMode
            ? nativeResidentRunning
            : pipeReady && pipeServer?.ActiveTarget is not null;
        var effectiveTypingReady = companionMode
            ? nativeResidentRunning
            : typingReady;
        var effectiveHotkeysReady = companionMode
            ? nativeResidentRunning
            : hotkeysReady;
        var nativeBackendPresent = File.Exists(
            Path.Combine(AppContext.BaseDirectory, "KeyinaEngine.dll"));
        var systemFailureCode = ReadinessMapper.IsSystemFailureCode(state.ErrorCode)
            ? state.ErrorCode
            : null;
        var health = new KeyinaHealthSnapshot(
            OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041),
            nativeBackendPresent,
            ComRegistered: true,
            TsfProfileRegistered: effectiveTypingReady,
            HostHealthy: effectiveTypingReady && effectiveHotkeysReady && systemFailureCode is null,
            IpcConnected: ipcConnected,
            EndToEndTypingPassed: endToEndTypingPassed,
            lastTypingTestAt,
            FocusedApplication: null,
            systemFailureCode);
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
            companionMode
                ? nativeResidentRunning
                    ? "Native resident connected"
                    : "Native resident unavailable"
                : ipcConnected
                    ? "Focused app connected"
                    : pipeReady
                        ? "Waiting for compatible app"
                        : "IPC unavailable",
            effectiveHotkeysReady ? "Resident active" : "Unavailable",
            effectiveTypingReady,
            configuration.Feedback?.Mode ?? FeedbackMode.Automatic)
        {
            Health = health,
            TranslationEnabled = configuration.TranslationEnabled,
            TranslationCredentialConfigured = translationCredentialConfigured,
            TranslationPreviewEnabled = configuration.TranslationPreviewEnabled,
            LibreTranslateCredentialConfigured = libreTranslateCredentialConfigured,
            TranslationProviders = configuration.TranslationProviders,
            TranslationHotkeyRegistered = translationHotkeyReady,
            TranslationTargetLanguage = configuration.TranslationTargetLanguage,
            Hotkeys = configuration.Hotkeys,
            Applications = configuration.Applications,
            Snippets = configuration.Snippets,
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
        lastTypingTestAt = DateTimeOffset.UtcNow;
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
        toggleVietnameseMenuItem.ShortcutKeyDisplayString = FormatShortcutDisplay(
            configuration.Hotkeys.ToggleVietnamese.Chord);
        toggleDictationMenuItem.Text = IsDictationActive
            ? "Dừng nhập bằng giọng nói"
            : "Bắt đầu nhập bằng giọng nói";
        toggleDictationMenuItem.Enabled = configuration.SpeechEnabled;
        toggleDictationMenuItem.ShortcutKeyDisplayString = FormatShortcutDisplay(
            configuration.Hotkeys.ToggleDictation.Chord);
        var translationProviderConfigured = IsTranslationProviderConfigured();
        translateSelectionMenuItem.Enabled = configuration.TranslationEnabled;
        translateSelectionMenuItem.Text =
            $"Dịch sang {TranslationLanguageCatalog.GetDisplayName(configuration.TranslationTargetLanguage)}";
        translateSelectionMenuItem.ShortcutKeyDisplayString =
            !configuration.TranslationEnabled
                ? string.Empty
                : !translationProviderConfigured
                    ? "Cần provider dịch"
                    : translationHotkeyReady
                        ? FormatShortcutDisplay(
                            configuration.Hotkeys.TranslateSelection.Chord)
                        : "Phím tắt xung đột";
        undoTranslationMenuItem.Enabled = translationCoordinator.CanUndo;
        undoTranslationMenuItem.ShortcutKeyDisplayString =
            translationCoordinator.CanUndo
                ? FormatShortcutDisplay(configuration.Hotkeys.UndoTranslation.Chord)
                : string.Empty;
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
        ReplaceMenuImage(translateSelectionMenuItem, FluentTrayMenu.CreateGlyph("\uE8C1", palette));
        ReplaceMenuImage(undoTranslationMenuItem, FluentTrayMenu.CreateGlyph("\uE7A7", palette));
        ReplaceMenuImage(startupMenuItem, FluentTrayMenu.CreateGlyph("\uE7E8", palette));
        ReplaceMenuImage(settingsMenuItem, FluentTrayMenu.CreateGlyph("\uE713", palette));
        ReplaceMenuImage(exitMenuItem, FluentTrayMenu.CreateGlyph("\uE7E8", palette, FluentTone.Error));
        if (notifyIcon.Icon is { } trayIcon)
        {
            ReplaceMenuImage(statusMenuItem, trayIcon.ToBitmap());
        }
        trayThemeMode = palette.Mode;
    }

    private static string FormatShortcutDisplay(HotkeyChord chord) =>
        HotkeyText.Format(chord).Replace(" + ", "+", StringComparison.Ordinal);

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

    private Task SaveConfigurationAsync(CancellationToken cancellationToken)
    {
        var snapshot = configuration;
        lock (configurationSaveSync)
        {
            pendingConfigurationSave = SaveAfterPreviousAsync(
                pendingConfigurationSave,
                snapshot,
                cancellationToken);
            return pendingConfigurationSave;
        }
    }

    private async Task SaveAfterPreviousAsync(
        Task previous,
        KeyinaConfiguration snapshot,
        CancellationToken cancellationToken)
    {
        try
        {
            await previous.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A failed older snapshot must not block a newer valid snapshot.
        }

        await configurationStore.SaveAsync(snapshot, cancellationToken)
            .ConfigureAwait(false);
        try
        {
            await runtimeInputProfileStore.PublishAsync(snapshot, cancellationToken)
                .ConfigureAwait(false);
            await runtimeSnippetProfileStore.PublishAsync(snapshot, cancellationToken)
                .ConfigureAwait(false);
            runtimeInputProfileReady = true;
        }
        catch (RuntimeInputProfileException)
        {
            runtimeInputProfileReady = false;
        }
        catch (RuntimeSnippetProfileException)
        {
            runtimeInputProfileReady = false;
        }
    }

    private void PublishRuntimeInputProfileAtStartup()
    {
        try
        {
            runtimeInputProfileStore.PublishAsync(
                    configuration,
                    CancellationToken.None)
                .GetAwaiter().GetResult();
            runtimeSnippetProfileStore.PublishAsync(
                    configuration,
                    CancellationToken.None)
                .GetAwaiter().GetResult();
            runtimeInputProfileReady = true;
        }
        catch (RuntimeInputProfileException)
        {
            runtimeInputProfileReady = false;
        }
        catch (RuntimeSnippetProfileException)
        {
            runtimeInputProfileReady = false;
        }
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

    private void WaitForPendingConfigurationSave()
    {
        Task pending;
        lock (configurationSaveSync)
        {
            pending = pendingConfigurationSave;
        }
        try
        {
            pending.GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Save failures are already mapped to stable host error codes.
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
        WaitForPendingConfigurationSave();
        lifetime.Cancel();

        CloseSettings();
        CloseTranslationPreview();
        dictationOverlayForm?.HideOverlay();
        dictationOverlayForm?.Close();
        dictationOverlayForm?.Dispose();
        dictationOverlayForm = null;
        snippetSuggestionOverlayForm?.HideOverlay();
        snippetSuggestionOverlayForm?.Close();
        snippetSuggestionOverlayForm?.Dispose();
        snippetSuggestionOverlayForm = null;
        firstRunForm?.Close();
        firstRunForm?.Dispose();
        firstRunForm = null;
        notifyIcon.Visible = false;

        feedbackCoordinator?.Dispose();
        feedbackCoordinator = null;
        modifierHook?.Dispose();
        modifierHook = null;
        typingHook?.Dispose();
        typingHook = null;
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

        translationCoordinator.Dispose();
        translationHttpClient?.Dispose();
        libreTranslationHttpClient?.Dispose();
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
