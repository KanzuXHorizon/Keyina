using System.Text;
using Keyina.Host.Configuration;
using Keyina.Host.Core.Configuration;
using Keyina.Host.Core.Hotkeys;

namespace Keyina.Host.Tests;

internal static class PortableSettingsServiceTests
{
    [KeyinaTest("portable settings export round trips preferences without credentials")]
    private static void ExportRoundTripsWithoutCredentials()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "keyina-settings.json");
        var configuration = KeyinaConfiguration.Default with
        {
            VietnameseEnabled = false,
            SpeechEnabled = true,
            TranslationEnabled = true,
            TranslationTargetLanguage = "JA",
            FirstRunCompleted = true,
            Hotkeys = HotkeyPreferences.Default with
            {
                TranslateSelection = HotkeyPreferences.Default.TranslateSelection with
                {
                    Chord = new HotkeyChord(
                        HotkeyModifiers.Control | HotkeyModifiers.Shift,
                        VirtualKey.K),
                },
            },
        };
        PortableSettingsService.ExportAsync(
                path,
                configuration,
                CancellationToken.None)
            .GetAwaiter().GetResult();
        var imported = PortableSettingsService.ImportAsync(
                path,
                CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.Equal(configuration.SchemaVersion, imported.SchemaVersion);
        AssertEx.Equal(configuration.VietnameseEnabled, imported.VietnameseEnabled);
        AssertEx.Equal(configuration.SpeechEnabled, imported.SpeechEnabled);
        AssertEx.Equal(configuration.Theme, imported.Theme);
        AssertEx.Equal(configuration.TranslationEnabled, imported.TranslationEnabled);
        AssertEx.Equal(
            configuration.TranslationTargetLanguage,
            imported.TranslationTargetLanguage);
        AssertEx.Equal(configuration.Hotkeys, imported.Hotkeys);
        AssertEx.Equal(configuration.FirstRunCompleted, imported.FirstRunCompleted);
        AssertEx.Equal(configuration.Snippets.Length, imported.Snippets.Length);
        var json = File.ReadAllText(path, Encoding.UTF8);
        foreach (var forbidden in new[]
                 {
                     "api_key",
                     "credential",
                     "transcript",
                     "selected_text",
                     "translated_text",
                 })
        {
            AssertEx.False(
                json.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                $"Portable settings leaked forbidden field: {forbidden}.");
        }
    }

    [KeyinaTest("portable settings import rejects unknown versions fields and oversized files")]
    private static void ImportRejectsUnsafeDocuments()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "keyina-settings.json");

        File.WriteAllText(path, "{\"format_version\":2,\"configuration\":{}}");
        AssertThrows<ConfigurationException>(() =>
            PortableSettingsService.ImportAsync(
                    path,
                    CancellationToken.None)
                .GetAwaiter().GetResult());

        File.WriteAllText(
            path,
            "{\"format_version\":1,\"configuration\":{},\"api_key\":\"secret\"}");
        AssertThrows<ConfigurationException>(() =>
            PortableSettingsService.ImportAsync(
                    path,
                    CancellationToken.None)
                .GetAwaiter().GetResult());

        File.WriteAllBytes(path, new byte[(1024 * 1024) + 1]);
        AssertThrows<ConfigurationException>(() =>
            PortableSettingsService.ImportAsync(
                    path,
                    CancellationToken.None)
                .GetAwaiter().GetResult());
    }

    [KeyinaTest("portable settings export requires an absolute JSON path")]
    private static void ExportRequiresSafePath()
    {
        AssertThrows<ArgumentException>(() =>
            PortableSettingsService.ExportAsync(
                    "settings.json",
                    KeyinaConfiguration.Default,
                    CancellationToken.None)
                .GetAwaiter().GetResult());
        AssertThrows<ArgumentException>(() =>
            PortableSettingsService.ExportAsync(
                    Path.Combine(Path.GetTempPath(), "settings.txt"),
                    KeyinaConfiguration.Default,
                    CancellationToken.None)
                .GetAwaiter().GetResult());
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"Keyina.PortableSettings.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
