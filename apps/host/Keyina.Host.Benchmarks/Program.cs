using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Keyina.Host.Core.Ipc;
using Keyina.Host.Core.Speech;
using Keyina.Host.Windows.Audio;
using Keyina.Host.Windows.Typing;
using Keyina.Speechmatics;

namespace Keyina.Host.Benchmarks;

internal static class Program
{
    private const int WarmupIterations = 5_000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public static int Main()
    {
        var finalJson =
            "{\"message\":\"AddTranscript\",\"metadata\":{\"transcript\":\"xin chào\",\"start_time\":0.0,\"end_time\":0.8}}"u8.ToArray();
        var transcriptEvent = new TranscriptEvent(
            TranscriptEventKind.Partial,
            "xin chào",
            0,
            0.8);
        var sessionId = new IpcSessionId(1, 2);
        var envelope = new IpcEnvelope(
            IpcMessageType.FinalTranscript,
            Flags: 0,
            sessionId,
            FocusGeneration: 3,
            Payload: "xin chào");
        var audioSource = CreateAudioSource();
        using var typingEngine = new NativeEngineClient();
        var inputSender = new CountingInputSender();
        var inputInjector = new UnicodeInputInjector(inputSender);
        var transformEdit = new HookEdit(1, "á", ConsumePhysicalKey: true);
        var hookNativeApi = new BenchmarkHookNativeApi();
        using var benchmarkHook = new VietnameseKeyboardHook(
            new NativeEngineClient(),
            inputInjector,
            hookNativeApi);
        benchmarkHook.Start(enabledInitially: true);

        var cases = new List<BenchmarkCase>
        {
            Measure(
                "speech_protocol_parse_final",
                iterations: 50_000,
                budgetP99Nanoseconds: 50_000,
                budgetAllocatedBytesPerOperation: 512,
                operation: () =>
                {
                    var parsed = SpeechmaticsProtocol.ParseServerMessage(finalJson);
                    return parsed.Text?.Length ?? 0;
                }),
            Measure(
                "transcript_partial_update",
                iterations: 50_000,
                budgetP99Nanoseconds: 50_000,
                budgetAllocatedBytesPerOperation: 512,
                operation: () =>
                {
                    var aggregator = new TranscriptAggregator();
                    var update = aggregator.Apply(transcriptEvent, sessionId, 3);
                    return update.PartialText.Length;
                }),
            Measure(
                "audio_convert_30ms_48khz_stereo",
                iterations: 10_000,
                budgetP99Nanoseconds: 1_000_000,
                budgetAllocatedBytesPerOperation: 4_096,
                operation: () =>
                {
                    var converter = new Pcm16MonoConverter(
                        new AudioSourceFormat(
                            48_000,
                            2,
                            AudioSampleEncoding.IeeeFloat));
                    return converter.Convert(audioSource).Length;
                }),
            Measure(
                "ipc_final_encode",
                iterations: 50_000,
                budgetP99Nanoseconds: 50_000,
                budgetAllocatedBytesPerOperation: 128,
                operation: () => IpcFrameCodec.Encode(envelope).Length),
        };

        TypingLatencyProfiler.Clear();
        TypingLatencyProfiler.SetEnabled(false);
        cases.Add(Measure(
            "typing_profiler_disabled_start",
            iterations: 200_000,
            budgetP99Nanoseconds: 500,
            budgetAllocatedBytesPerOperation: 0,
            operation: () => checked((int)TypingLatencyProfiler.Start())));

        TypingLatencyProfiler.Clear();
        TypingLatencyProfiler.SetEnabled(true);
        cases.Add(Measure(
            "typing_profiler_enabled_record",
            iterations: 100_000,
            budgetP99Nanoseconds: 2_000,
            budgetAllocatedBytesPerOperation: 0,
            operation: () =>
            {
                var startedAt = TypingLatencyProfiler.Start();
                TypingLatencyProfiler.Record(
                    TypingLatencyStage.EngineProcess,
                    startedAt);
                return 1;
            }));
        TypingLatencyProfiler.SetEnabled(false);
        TypingLatencyProfiler.Clear();

        cases.Add(Measure(
            "typing_foreground_context_snapshot",
            iterations: 100_000,
            budgetP99Nanoseconds: 20_000,
            budgetAllocatedBytesPerOperation: 0,
            operation: () =>
            {
                var context = WindowsTypingContextProbe.Capture();
                return context.ForegroundProcessId ^ context.FocusWindow.GetHashCode();
            }));
        cases.Add(Measure(
            "typing_native_engine_literal",
            iterations: 100_000,
            budgetP99Nanoseconds: 20_000,
            budgetAllocatedBytesPerOperation: 0,
            operation: () =>
            {
                typingEngine.Reset();
                var edit = typingEngine.Process(
                    NativeEngineKeyKind.Character,
                    new Rune('b'));
                return edit.InsertText.Length + edit.BackspaceCount;
            }));
        cases.Add(Measure(
            "typing_native_engine_transform",
            iterations: 100_000,
            budgetP99Nanoseconds: 30_000,
            budgetAllocatedBytesPerOperation: 0,
            operation: () =>
            {
                typingEngine.Reset();
                _ = typingEngine.Process(
                    NativeEngineKeyKind.Character,
                    new Rune('a'));
                var edit = typingEngine.Process(
                    NativeEngineKeyKind.Character,
                    new Rune('s'));
                return edit.InsertText.Length + edit.BackspaceCount;
            }));
        cases.Add(Measure(
            "typing_injector_transform_prepare",
            iterations: 100_000,
            budgetP99Nanoseconds: 10_000,
            budgetAllocatedBytesPerOperation: 0,
            operation: () =>
            {
                inputInjector.Apply(transformEdit);
                return inputSender.LastCount;
            }));
        cases.Add(Measure(
            "typing_hook_literal",
            iterations: 100_000,
            budgetP99Nanoseconds: 20_000,
            budgetAllocatedBytesPerOperation: 0,
            operation: () =>
            {
                benchmarkHook.Reset();
                return hookNativeApi.Dispatch('B', 'b') ? 1 : 0;
            }));
        cases.Add(Measure(
            "typing_hook_transform",
            iterations: 100_000,
            budgetP99Nanoseconds: 30_000,
            budgetAllocatedBytesPerOperation: 0,
            operation: () =>
            {
                benchmarkHook.Reset();
                _ = hookNativeApi.Dispatch('A', 'a');
                var handled = hookNativeApi.Dispatch('S', 's');
                return inputSender.LastCount + (handled ? 1 : 0);
            }));

        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var document = new BenchmarkDocument(
            SchemaVersion: 1,
            Environment: new BenchmarkEnvironment(
                Runtime: RuntimeInformation.FrameworkDescription,
                OperatingSystem: RuntimeInformation.OSDescription,
                Architecture: RuntimeInformation.ProcessArchitecture.ToString(),
                ProcessorCount: Environment.ProcessorCount,
                BuildConfiguration: IsDebugBuild ? "debug" : "release",
                WarmupIterations),
            Cases: cases,
            Process: new ProcessSnapshot(
                process.WorkingSet64,
                process.PrivateMemorySize64,
                GC.GetTotalMemory(forceFullCollection: false),
                process.Threads.Count),
            Checksum: cases.Sum(result => result.Checksum));

        Console.WriteLine(JsonSerializer.Serialize(document, JsonOptions));
        return cases.All(result => result.BudgetPass) ? 0 : 1;
    }

    private static BenchmarkCase Measure(
        string name,
        int iterations,
        long budgetP99Nanoseconds,
        double budgetAllocatedBytesPerOperation,
        Func<int> operation)
    {
        for (var index = 0; index < WarmupIterations; index++)
        {
            _ = operation();
        }

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocationStart = GC.GetAllocatedBytesForCurrentThread();
        long checksum = 0;
        for (var index = 0; index < iterations; index++)
        {
            checksum += operation();
        }
        var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocationStart;

        var samples = new long[iterations];
        for (var index = 0; index < iterations; index++)
        {
            var start = Stopwatch.GetTimestamp();
            checksum += operation();
            samples[index] = ToNanoseconds(Stopwatch.GetTimestamp() - start);
        }
        Array.Sort(samples);

        var median = Percentile(samples, 0.50);
        var p95 = Percentile(samples, 0.95);
        var p99 = Percentile(samples, 0.99);
        var allocatedBytesPerOperation = allocatedBytes / (double)iterations;
        return new BenchmarkCase(
            name,
            iterations,
            median,
            p95,
            p99,
            samples[^1],
            allocatedBytesPerOperation,
            budgetP99Nanoseconds,
            budgetAllocatedBytesPerOperation,
            p99 <= budgetP99Nanoseconds &&
                allocatedBytesPerOperation <= budgetAllocatedBytesPerOperation,
            checksum);
    }

    private static long Percentile(long[] sortedSamples, double percentile)
    {
        var index = (int)Math.Ceiling((sortedSamples.Length - 1) * percentile);
        return sortedSamples[index];
    }

    private static long ToNanoseconds(long timestampDelta) =>
        checked((long)Math.Round(
            timestampDelta * (1_000_000_000.0 / Stopwatch.Frequency),
            MidpointRounding.AwayFromZero));

    private static byte[] CreateAudioSource()
    {
        const int frames = 1_440;
        var samples = new float[frames * 2];
        for (var frame = 0; frame < frames; frame++)
        {
            samples[frame * 2] = MathF.Sin(frame * 0.03f) * 0.5f;
            samples[(frame * 2) + 1] = MathF.Cos(frame * 0.02f) * 0.5f;
        }

        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return bytes;
    }

#if DEBUG
    private const bool IsDebugBuild = true;
#else
    private const bool IsDebugBuild = false;
#endif

    private sealed record BenchmarkDocument(
        int SchemaVersion,
        BenchmarkEnvironment Environment,
        IReadOnlyList<BenchmarkCase> Cases,
        ProcessSnapshot Process,
        long Checksum);

    private sealed record BenchmarkEnvironment(
        string Runtime,
        string OperatingSystem,
        string Architecture,
        int ProcessorCount,
        string BuildConfiguration,
        int WarmupIterations);

    private sealed record BenchmarkCase(
        string Name,
        int Iterations,
        long MedianNanoseconds,
        long P95Nanoseconds,
        long P99Nanoseconds,
        long MaximumNanoseconds,
        double AllocatedBytesPerOperation,
        long BudgetP99Nanoseconds,
        double BudgetAllocatedBytesPerOperation,
        bool BudgetPass,
        long Checksum);

    private sealed record ProcessSnapshot(
        long WorkingSetBytes,
        long PrivateMemoryBytes,
        long ManagedHeapBytes,
        int ThreadCount);

    private sealed class CountingInputSender : UnicodeInputInjector.IInputSender
    {
        public int LastCount { get; private set; }

        public void Send(
            ReadOnlySpan<UnicodeInputInjector.KeyboardInputEvent> inputs) =>
            LastCount = inputs.Length;
    }

    private sealed class BenchmarkHookNativeApi : IVietnameseKeyboardHookNativeApi
    {
        private Func<VietnameseKeyboardEvent, bool>? callback;

        public IDisposable Install(Func<VietnameseKeyboardEvent, bool> value)
        {
            callback = value;
            return NoopDisposable.Instance;
        }

        public IDisposable InstallPointerReset(Action callback) =>
            NoopDisposable.Instance;

        public VietnameseTypingContext GetTypingContext() => new(
            ForegroundProcessId: 1,
            FocusWindow: (nint)1,
            ShouldBypassTyping: false);

        public bool Dispatch(char virtualKey, char character) =>
            callback?.Invoke(new VietnameseKeyboardEvent(
                virtualKey,
                IsKeyDown: true,
                IsInjected: false,
                ExtraInfo: 0,
                Shift: false,
                Control: false,
                Alt: false,
                Windows: false,
                new Rune(character))) ?? false;
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static NoopDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
