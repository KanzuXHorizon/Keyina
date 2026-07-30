using System.Text.Json;
using Keyina.Host.Core;
using Keyina.Host.Diagnostics;
using Keyina.Host.Hotkeys;
using Keyina.Host.Runtime;
using Keyina.Host.Speech;
using Keyina.Host.UI;
using Keyina.Host.UI.Fluent;
using Keyina.Host.Windows.Typing;

namespace Keyina.Host;

internal static class Program
{
    private static readonly JsonSerializerOptions ResourceJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private const int AlreadyRunningExitCode = 17;
    private const string HostMutexName = "Local\\Keyina.Host";

    private sealed record ResidentInputResourceSnapshot(
        double HookStartupMilliseconds,
        double DurationMilliseconds,
        long WorkingSetBytes,
        long WorkingSetDeltaBytes,
        long PrivateMemoryBytes,
        long PrivateMemoryDeltaBytes,
        long ManagedHeapBytes,
        int ThreadCount,
        int ThreadCountDelta,
        int HandleCount,
        int HandleCountDelta,
        int ProcessorCount,
        double CpuTimeMilliseconds,
        double AverageCpuPercent,
        bool TypingHookRunning,
        long ProcessedPhysicalEventCount,
        bool MeasurementContaminatedByInput);

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--self-test", StringComparer.Ordinal))
        {
            Console.WriteLine($"{BuildInfo.ProductName} {BuildInfo.ProductVersion}");
            return 0;
        }

        if (args.Contains("--speech-self-test", StringComparer.Ordinal))
        {
            var result = SpeechSelfTest.RunAsync(CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            Console.WriteLine(result.Code);
            return result.Success ? 0 : 1;
        }

        if (args.Contains("--resource-self-test", StringComparer.Ordinal))
        {
            _ = HostResourceProbe.CaptureAsync(
                    TimeSpan.FromMilliseconds(500),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            using var process = System.Diagnostics.Process.GetCurrentProcess();
            process.Refresh();
            var baselineWorkingSet = process.WorkingSet64;
            var baselinePrivateMemory = process.PrivateMemorySize64;
            var baselineThreadCount = process.Threads.Count;
            var baselineHandleCount = process.HandleCount;

            var startupTimer = System.Diagnostics.Stopwatch.StartNew();
            using var hook = new VietnameseKeyboardHook();
            hook.Start(enabledInitially: false);
            startupTimer.Stop();

            Thread.Sleep(500);
            var snapshot = HostResourceProbe.CaptureAsync(
                    TimeSpan.FromSeconds(5),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            var result = new ResidentInputResourceSnapshot(
                startupTimer.Elapsed.TotalMilliseconds,
                snapshot.DurationMilliseconds,
                snapshot.WorkingSetBytes,
                snapshot.WorkingSetBytes - baselineWorkingSet,
                snapshot.PrivateMemoryBytes,
                snapshot.PrivateMemoryBytes - baselinePrivateMemory,
                snapshot.ManagedHeapBytes,
                snapshot.ThreadCount,
                snapshot.ThreadCount - baselineThreadCount,
                snapshot.HandleCount,
                snapshot.HandleCount - baselineHandleCount,
                snapshot.ProcessorCount,
                snapshot.CpuTimeMilliseconds,
                snapshot.AverageCpuPercent,
                hook.IsRunning,
                hook.ProcessedPhysicalEventCount,
                hook.ProcessedPhysicalEventCount != 0);
            Console.WriteLine(JsonSerializer.Serialize(result, ResourceJsonOptions));
            return 0;
        }

        if (args.Contains("--hotkey-self-test", StringComparer.Ordinal))
        {
            var result = HotkeySelfTest.Run();
            Console.WriteLine(result.Code);
            return result.Success ? 0 : 1;
        }

        FluentTheme.InitializeApplicationColorMode();

        var galleryIndex = Array.FindIndex(
            args,
            argument => string.Equals(argument, "--render-settings-gallery", StringComparison.Ordinal));
        if (galleryIndex >= 0)
        {
            if (galleryIndex + 1 >= args.Length)
            {
                Console.Error.WriteLine("--render-settings-gallery requires an output directory.");
                return 2;
            }
            ApplicationConfiguration.Initialize();
            var outputDirectory = Path.GetFullPath(args[galleryIndex + 1]);
            var paths = SettingsScreenshotRenderer.RenderGallery(
                outputDirectory,
                SettingsSnapshot.Sample);
            foreach (var path in paths)
            {
                Console.WriteLine(path);
            }
            return 0;
        }

        if (!SingleInstanceGuard.TryAcquire(HostMutexName, out var guard))
        {
            return AlreadyRunningExitCode;
        }

        using (guard)
        {
            ApplicationConfiguration.Initialize();
            using var context = new KeyinaApplicationContext(
                KeyinaRuntimeOptions.CreateProduction(
                    showSettingsOnStart: args.Contains("--show-settings", StringComparer.Ordinal)));
            Application.Run(context);
            return 0;
        }
    }
}
