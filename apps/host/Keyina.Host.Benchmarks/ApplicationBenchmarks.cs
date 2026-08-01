using System.Threading.Channels;
using System.Windows.Forms;
using Keyina.Host.Core.Configuration;
using Keyina.Host.Core.Ipc;
using Keyina.Host.Core.Translation;
using Keyina.Host.Speech;
using Keyina.Host.Translation;
using Keyina.Host.UI;
using Keyina.Host.Windows.Audio;
using Keyina.Speechmatics;

namespace Keyina.Host.Benchmarks;

internal static class ApplicationBenchmarks
{
    private const int SnippetCount = 1_000;

    internal static IReadOnlyList<BenchmarkCase> RunSnippetUi(
        int warmupIterations,
        int measuredIterations)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warmupIterations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(measuredIterations);
        return
        [
            MeasureSnippetPopulation(
                Math.Min(warmupIterations, 10),
                Math.Min(measuredIterations, 50)),
            MeasureUnchangedSnippetSnapshot(
                Math.Min(warmupIterations, 10),
                Math.Min(measuredIterations, 50)),
        ];
    }

    internal static async Task<IReadOnlyList<BenchmarkCase>> RunAsync(
        int warmupIterations,
        int measuredIterations)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(warmupIterations);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(measuredIterations);

        var settingsWarmup = Math.Min(warmupIterations, 3);
        var settingsIterations = Math.Min(measuredIterations, 10);
        var asyncWarmup = Math.Min(warmupIterations, 5);
        var asyncIterations = Math.Min(measuredIterations, 50);
        var largeSettingsSnapshot = CreateSnippetSnapshot("startup");
        var cases = new List<BenchmarkCase>
        {
            BenchmarkReport.Measure(
                "application_settings_construct_sample",
                settingsWarmup,
                settingsIterations,
                () => ConstructSettings(SettingsSnapshot.Sample)),
            BenchmarkReport.Measure(
                "application_settings_construct_1000_snippets",
                settingsWarmup,
                settingsIterations,
                () => ConstructSettings(largeSettingsSnapshot)),
            MeasureSnippetPopulation(settingsWarmup, settingsIterations),
            MeasureUnchangedSnippetSnapshot(settingsWarmup, settingsIterations),
        };

        cases.Add(await BenchmarkReport.MeasureAsync(
                "application_speech_start_stop_stub",
                asyncWarmup,
                asyncIterations,
                RunSpeechStartStopAsync)
            .ConfigureAwait(false));
        cases.Add(await BenchmarkReport.MeasureAsync(
                "application_translation_preview_stub",
                asyncWarmup,
                asyncIterations,
                RunTranslationPreviewAsync)
            .ConfigureAwait(false));
        return cases;
    }

    private static long ConstructSettings(SettingsSnapshot snapshot)
    {
        using var form = new SettingsForm(snapshot, SettingsActions.NoOp);
        return CountControls(form);
    }

    private static BenchmarkCase MeasureSnippetPopulation(
        int warmupIterations,
        int measuredIterations)
    {
        var emptySnapshot = SettingsSnapshot.Sample with
        {
            CustomSnippetCount = 0,
            Snippets = Array.Empty<SnippetConfiguration>(),
        };
        var firstSnapshot = CreateSnippetSnapshot("a");
        var secondSnapshot = CreateSnippetSnapshot("b");
        using var form = new SettingsForm(emptySnapshot, SettingsActions.NoOp);
        form.OpenSection("snippets");
        var snippetList = (FlowLayoutPanel)form.Controls
            .Find("snippetsList", searchAllChildren: true)
            .Single();
        var useFirst = false;

        return BenchmarkReport.Measure(
            "application_settings_apply_1000_snippets",
            warmupIterations,
            measuredIterations,
            () =>
            {
                useFirst = !useFirst;
                form.ApplySnapshot(useFirst ? firstSnapshot : secondSnapshot);
                return snippetList.Controls.Count + (useFirst ? 1 : 2);
            });
    }

    private static BenchmarkCase MeasureUnchangedSnippetSnapshot(
        int warmupIterations,
        int measuredIterations)
    {
        var snapshot = CreateSnippetSnapshot("stable");
        var listening = snapshot with
        {
            Listening = true,
            StatusMessage = "Listening",
        };
        var idle = snapshot with
        {
            Listening = false,
            StatusMessage = "Ready",
        };
        using var form = new SettingsForm(snapshot, SettingsActions.NoOp);
        form.OpenSection("snippets");
        var snippetList = (FlowLayoutPanel)form.Controls
            .Find("snippetsList", searchAllChildren: true)
            .Single();
        var useListening = false;

        return BenchmarkReport.Measure(
            "application_settings_apply_unchanged_1000_snippets",
            warmupIterations,
            measuredIterations,
            () =>
            {
                useListening = !useListening;
                form.ApplySnapshot(useListening ? listening : idle);
                return snippetList.Controls.Count + (useListening ? 1 : 2);
            });
    }

    private static SettingsSnapshot CreateSnippetSnapshot(string suffix)
    {
        var snippets = Enumerable.Range(0, SnippetCount)
            .Select(index => new SnippetConfiguration(
                $";kbench{suffix}{index:D4}",
                $"Benchmark expansion {suffix} {index:D4}",
                CaseSensitive: false,
                PreserveDelimiter: false,
                Delimiters: " ",
                AllowedApplications: [],
                ExcludedApplications: []))
            .ToArray();
        return SettingsSnapshot.Sample with
        {
            CustomSnippetCount = snippets.Length,
            Snippets = snippets,
        };
    }

    private static async Task<long> RunSpeechStartStopAsync()
    {
        var publishedEvents = 0L;
        await using var coordinator = new DictationCoordinator(
            new BenchmarkSpeechSessionFactory(),
            new EmptyAudioCapture(),
            new NullEnvelopeWriter(),
            new DictationOverlayModel(),
            _ => publishedEvents++,
            () => 1,
            TimeSpan.FromSeconds(1));
        await coordinator.StartAsync(
                "benchmark-key",
                new IpcSessionId(1, 2),
                CancellationToken.None)
            .ConfigureAwait(false);
        await coordinator.StopAsync(CancellationToken.None).ConfigureAwait(false);
        return publishedEvents + 1;
    }

    private static async Task<long> RunTranslationPreviewAsync()
    {
        using var coordinator = new TranslationCoordinator(
            new BenchmarkSelectedTextAccessor(),
            new BenchmarkTranslationProvider(),
            clock: static () => DateTimeOffset.UnixEpoch);
        var outcome = await coordinator.TranslateSelectionAsync(
                "benchmark-key",
                "VI",
                CancellationToken.None,
                preview: true)
            .ConfigureAwait(false);
        if (outcome.Status != TranslationOutcomeStatus.PreviewReady || outcome.Preview is null)
        {
            throw new InvalidOperationException("Translation preview benchmark did not produce a preview.");
        }
        return outcome.Preview.TranslatedText.Length + 1;
    }

    private static int CountControls(Control root)
    {
        var count = root.Controls.Count;
        foreach (Control child in root.Controls)
        {
            count += CountControls(child);
        }
        return count;
    }

    private sealed class BenchmarkSpeechSessionFactory : ISpeechmaticsSessionFactory
    {
        public ISpeechmaticsRealtimeSession Create(string apiKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
            return new BenchmarkRealtimeSession();
        }
    }

    private sealed class BenchmarkRealtimeSession : ISpeechmaticsRealtimeSession
    {
        private readonly Channel<SpeechEvent> events = Channel.CreateUnbounded<SpeechEvent>();

        public SpeechmaticsSessionState State { get; private set; } =
            SpeechmaticsSessionState.Created;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            State = SpeechmaticsSessionState.Started;
            return Task.CompletedTask;
        }

        public ValueTask SendAudioAsync(
            ReadOnlyMemory<byte> audio,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }

        public ValueTask<SpeechEvent> ReadEventAsync(CancellationToken cancellationToken) =>
            events.Reader.ReadAsync(cancellationToken);

        public Task StopAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            State = SpeechmaticsSessionState.Stopped;
            events.Writer.TryWrite(new SpeechEvent
            {
                Kind = SpeechEventKind.EndOfTranscript,
            });
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            State = SpeechmaticsSessionState.Disposed;
            events.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class EmptyAudioCapture : IAudioCapture
    {
        private readonly Channel<ReadOnlyMemory<byte>> channel =
            Channel.CreateUnbounded<ReadOnlyMemory<byte>>();

        public IAsyncEnumerable<ReadOnlyMemory<byte>> CaptureAsync(
            CancellationToken cancellationToken) =>
            channel.Reader.ReadAllAsync(cancellationToken);
    }

    private sealed class NullEnvelopeWriter : IIpcEnvelopeWriter
    {
        public ValueTask WriteAsync(IpcEnvelope envelope, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BenchmarkSelectedTextAccessor : ISelectedTextAccessor
    {
        public Task<SelectedTextCapture?> CaptureAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<SelectedTextCapture?>(new SelectedTextCapture(
                "benchmark source text",
                ForegroundWindow: 1,
                FocusedWindow: 2));
        }

        public bool TryReplace(SelectedTextCapture selectedText, string translatedText) => true;
    }

    private sealed class BenchmarkTranslationProvider : ITranslationProvider
    {
        public Task<TranslationResult> TranslateAsync(
            string apiKey,
            TranslationRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
            return Task.FromResult(new TranslationResult(
                "văn bản benchmark",
                "EN",
                "benchmark"));
        }
    }
}
