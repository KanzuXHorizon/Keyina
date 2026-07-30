using System.Text;
using Keyina.Host.Configuration;
using Keyina.Host.Core.Configuration;
using Keyina.Host.Core.Feedback;

namespace Keyina.Host.Tests;

internal static class ConfigurationStoreTests
{
    [KeyinaTest("configuration store round trips validated settings atomically")]
    private static void ConfigurationRoundTrips()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new AtomicConfigurationStore(path);
        var configuration = KeyinaConfiguration.Default with
        {
            VietnameseEnabled = false,
            SpeechEnabled = true,
            Theme = KeyinaTheme.Dark,
            Snippets =
            [
                new SnippetConfiguration(
                    ";mail",
                    "hello@example.com",
                    CaseSensitive: false,
                    PreserveDelimiter: true,
                    Delimiters: " \t\r\n.,!?",
                    AllowedApplications: [],
                    ExcludedApplications: ["password-manager"]),
            ],
        };

        store.SaveAsync(configuration, CancellationToken.None)
            .GetAwaiter().GetResult();
        var loaded = store.LoadAsync(CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.Equal(configuration.SchemaVersion, loaded.SchemaVersion);
        AssertEx.Equal(configuration.VietnameseEnabled, loaded.VietnameseEnabled);
        AssertEx.Equal(configuration.SpeechEnabled, loaded.SpeechEnabled);
        AssertEx.Equal(configuration.Theme, loaded.Theme);
        AssertEx.Equal(configuration.Feedback, loaded.Feedback);
        AssertEx.Equal(configuration.Snippets.Length, loaded.Snippets.Length);
        AssertEx.Equal(configuration.Snippets[0].Trigger, loaded.Snippets[0].Trigger);
        AssertEx.Equal(configuration.Snippets[0].Expansion, loaded.Snippets[0].Expansion);
        AssertEx.True(
            configuration.Snippets[0].ExcludedApplications.SequenceEqual(
                loaded.Snippets[0].ExcludedApplications,
                StringComparer.Ordinal),
            "Snippet application scope changed during round trip.");
        AssertEx.True(File.Exists(path), "Configuration file was not created.");
        AssertEx.True(!File.Exists(path + ".tmp"), "Temporary file remained after save.");
        AssertEx.True(
            !File.ReadAllText(path, Encoding.UTF8).Contains("api_key", StringComparison.OrdinalIgnoreCase),
            "Configuration unexpectedly contained a secret field.");
    }

    [KeyinaTest("configuration store rejects unknown fields versions malformed JSON and duplicate triggers")]
    private static void InvalidConfigurationIsRejected()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new AtomicConfigurationStore(path);

        File.WriteAllText(path, "{\"schema_version\":2,\"vietnamese_enabled\":true,\"speech_enabled\":false,\"theme\":\"system\",\"snippets\":[]}");
        AssertThrows<ConfigurationException>(() => store.LoadAsync(CancellationToken.None).GetAwaiter().GetResult());

        File.WriteAllText(path, "{\"schema_version\":1,\"vietnamese_enabled\":true,\"speech_enabled\":false,\"theme\":\"system\",\"snippets\":[],\"api_key\":\"secret\"}");
        AssertThrows<ConfigurationException>(() => store.LoadAsync(CancellationToken.None).GetAwaiter().GetResult());

        File.WriteAllText(path, "{");
        AssertThrows<ConfigurationException>(() => store.LoadAsync(CancellationToken.None).GetAwaiter().GetResult());

        var duplicate = KeyinaConfiguration.Default with
        {
            Snippets =
            [
                new SnippetConfiguration(";x", "one", false, true, " ", [], []),
                new SnippetConfiguration(";X", "two", false, true, " ", [], []),
            ],
        };
        AssertThrows<ConfigurationException>(() =>
            store.SaveAsync(duplicate, CancellationToken.None).GetAwaiter().GetResult());
    }

    [KeyinaTest("schema one configuration without feedback uses automatic defaults")]
    private static void LegacySchemaOneUsesAutomaticFeedback()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new AtomicConfigurationStore(path);
        File.WriteAllText(
            path,
            "{\"schema_version\":1,\"vietnamese_enabled\":true,\"speech_enabled\":false,\"theme\":\"system\",\"snippets\":[]}");

        var loaded = store.LoadAsync(CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.Equal(FeedbackPreferences.Default, loaded.Feedback);
    }

    [KeyinaTest("configuration rejects invalid feedback mode")]
    private static void InvalidFeedbackModeIsRejected()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        var store = new AtomicConfigurationStore(path);
        var invalid = KeyinaConfiguration.Default with
        {
            Feedback = new FeedbackPreferences((FeedbackMode)999),
        };

        AssertThrows<ConfigurationException>(() =>
            store.SaveAsync(invalid, CancellationToken.None).GetAwaiter().GetResult());
    }

    [KeyinaTest("configuration store returns defaults when file is missing and ignores orphaned temp files")]
    private static void MissingFileUsesDefaults()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        File.WriteAllText(path + ".tmp", "partial");
        var store = new AtomicConfigurationStore(path);

        var loaded = store.LoadAsync(CancellationToken.None)
            .GetAwaiter().GetResult();
        AssertEx.Equal(KeyinaConfiguration.Default, loaded);
    }

    [KeyinaTest("configuration path is current-user local and contains no roaming or machine scope")]
    private static void ProductionPathIsUserLocal()
    {
        var path = ConfigurationPaths.GetProductionPath();
        AssertEx.True(Path.IsPathFullyQualified(path), "Production config path was not absolute.");
        AssertEx.True(path.EndsWith(Path.Combine("Keyina", "settings.json"), StringComparison.OrdinalIgnoreCase),
            "Production config path did not end with Keyina/settings.json.");
        AssertEx.True(!path.Contains("ProgramData", StringComparison.OrdinalIgnoreCase),
            "Production config path used machine-wide storage.");
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
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
                $"Keyina.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
