using Keyina.Host.Configuration;
using Keyina.Host.Core.Configuration;
using Keyina.Host.Core.Hotkeys;

namespace Keyina.Host.Tests;

internal static class RuntimeInputProfileStoreTests
{
    [KeyinaTest("runtime profile store publishes exact codec bytes atomically")]
    private static void PublishesExactBytes()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "runtime-input.bin");
        var store = new RuntimeInputProfileStore(path);
        var configuration = KeyinaConfiguration.Default with
        {
            VietnameseEnabled = false,
            Hotkeys = HotkeyPreferences.Default.WithChord(
                HotkeyCommand.UndoTranslation,
                new HotkeyChord(
                    HotkeyModifiers.Control | HotkeyModifiers.Shift,
                    VirtualKey.F10)),
        };

        store.PublishAsync(configuration, CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.True(File.Exists(path), "Runtime profile was not created.");
        AssertEx.True(
            File.ReadAllBytes(path).AsSpan().SequenceEqual(
                RuntimeInputProfileCodec.Encode(configuration)),
            "Published runtime profile did not match the codec bytes.");
        AssertEx.False(File.Exists(path + ".tmp"), "Runtime profile temp file remained.");
    }

    [KeyinaTest("runtime profile store replaces an existing snapshot atomically")]
    private static void ReplacesExistingSnapshot()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "runtime-input.bin");
        var store = new RuntimeInputProfileStore(path);
        store.PublishAsync(KeyinaConfiguration.Default, CancellationToken.None)
            .GetAwaiter().GetResult();
        var updated = KeyinaConfiguration.Default with
        {
            VietnameseEnabled = false,
            SpeechEnabled = true,
            TranslationEnabled = true,
        };

        store.PublishAsync(updated, CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.True(
            File.ReadAllBytes(path).AsSpan().SequenceEqual(
                RuntimeInputProfileCodec.Encode(updated)),
            "Existing runtime profile was not atomically replaced.");
    }

    [KeyinaTest("runtime profile store preserves the previous snapshot when replacement fails")]
    private static void FailedReplacementPreservesPreviousSnapshot()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "runtime-input.bin");
        var store = new RuntimeInputProfileStore(path);
        store.PublishAsync(KeyinaConfiguration.Default, CancellationToken.None)
            .GetAwaiter().GetResult();
        var original = File.ReadAllBytes(path);
        var updated = KeyinaConfiguration.Default with { VietnameseEnabled = false };

        using (new FileStream(
                   path,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read))
        {
            AssertThrows<RuntimeInputProfileException>(() =>
                store.PublishAsync(updated, CancellationToken.None)
                    .GetAwaiter().GetResult());
        }

        AssertEx.True(
            File.ReadAllBytes(path).AsSpan().SequenceEqual(original),
            "Failed replacement changed the previous runtime profile.");
        AssertEx.False(File.Exists(path + ".tmp"), "Failed replacement left a temp file.");
    }

    [KeyinaTest("runtime profile production path is current-user local")]
    private static void ProductionPathIsCurrentUserLocal()
    {
        var path = ConfigurationPaths.GetRuntimeInputProfilePath();

        AssertEx.True(Path.IsPathFullyQualified(path), "Runtime profile path was not absolute.");
        AssertEx.True(
            path.EndsWith(
                Path.Combine("Keyina", "runtime-input.bin"),
                StringComparison.OrdinalIgnoreCase),
            "Runtime profile path did not end with Keyina/runtime-input.bin.");
        AssertEx.False(
            path.Contains("ProgramData", StringComparison.OrdinalIgnoreCase),
            "Runtime profile used machine-wide storage.");
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
                $"Keyina.RuntimeProfile.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
