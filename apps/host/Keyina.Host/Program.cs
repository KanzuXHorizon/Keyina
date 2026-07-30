using System.Text.Json;
using Keyina.Host.Core;
using Keyina.Host.Diagnostics;
using Keyina.Host.Hotkeys;
using Keyina.Host.Runtime;
using Keyina.Host.Speech;
using Keyina.Host.UI;
using Keyina.Host.UI.Fluent;

namespace Keyina.Host;

internal static class Program
{
    private static readonly JsonSerializerOptions ResourceJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private const int AlreadyRunningExitCode = 17;
    private const string HostMutexName = "Local\\Keyina.Host";

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
            var snapshot = HostResourceProbe.CaptureAsync(
                    TimeSpan.FromSeconds(3),
                    CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            Console.WriteLine(JsonSerializer.Serialize(snapshot, ResourceJsonOptions));
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
