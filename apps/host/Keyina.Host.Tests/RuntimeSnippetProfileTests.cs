using Keyina.Host.Configuration;
using Keyina.Host.Core.Configuration;
using Keyina.Host.Core.Snippets;

namespace Keyina.Host.Tests;

internal static class RuntimeSnippetProfileTests
{
    [KeyinaTest("runtime snippet profile includes built ins custom variables and delimiter policy")]
    private static void RoundTripIncludesBuiltInsAndCustomSnippets()
    {
        var configuration = KeyinaConfiguration.Default with
        {
            FirstRunCompleted = true,
            Snippets =
            [
                new SnippetConfiguration(
                    ";aws",
                    "Released ${date} at ${time}",
                    CaseSensitive: false,
                    PreserveDelimiter: false,
                    Delimiters: " \t",
                    AllowedApplications: ["Code.exe"],
                    ExcludedApplications: ["Blocked.exe"]),
            ],
        };

        var encoded = RuntimeSnippetProfileCodec.Encode(configuration);
        var decoded = RuntimeSnippetProfileCodec.Decode(encoded);

        AssertEx.Equal(6, decoded.Entries.Count);
        var toggleVietnamese = decoded.Entries.Single(entry => entry.Trigger == ";kvi");
        AssertEx.Equal(SnippetCommand.ToggleVietnamese, toggleVietnamese.Command);
        AssertEx.True(!toggleVietnamese.PreserveDelimiter,
            "Built-in command should consume its activation delimiter.");

        var custom = decoded.Entries.Single(entry => entry.Trigger == ";aws");
        AssertEx.Equal("Released ${date} at ${time}", custom.Expansion);
        AssertEx.True(!custom.CaseSensitive, "Case policy was not retained.");
        AssertEx.True(!custom.PreserveDelimiter, "Delimiter policy was not retained.");
        AssertEx.True(
            custom.Delimiters.ToHashSet().SetEquals([' ', '\t']),
            "Delimiter set was not retained.");
        AssertEx.Equal(
            RuntimeSnippetProfileCodec.HashApplicationId("code.exe"),
            custom.AllowedApplicationHashes.Single());
        AssertEx.Equal(
            RuntimeSnippetProfileCodec.HashApplicationId("BLOCKED.EXE"),
            custom.ExcludedApplicationHashes.Single());
    }

    [KeyinaTest("runtime snippet profile store publishes exact codec bytes atomically")]
    private static void StorePublishesExactBytes()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "runtime-snippets.bin");
        var store = new RuntimeSnippetProfileStore(path);
        var configuration = KeyinaConfiguration.Default with
        {
            FirstRunCompleted = true,
            Snippets =
            [
                new SnippetConfiguration(
                    ";hello",
                    "Hello ${datetime}",
                    CaseSensitive: true,
                    PreserveDelimiter: true,
                    Delimiters: " ",
                    AllowedApplications: [],
                    ExcludedApplications: []),
            ],
        };

        store.PublishAsync(configuration, CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.True(File.Exists(path), "Runtime snippet profile was not created.");
        AssertEx.True(
            File.ReadAllBytes(path).AsSpan().SequenceEqual(
                RuntimeSnippetProfileCodec.Encode(configuration)),
            "Published runtime snippet profile did not match codec bytes.");
        AssertEx.False(File.Exists(path + ".tmp"),
            "Runtime snippet profile temp file remained.");
    }

    [KeyinaTest("runtime snippet profile rejects corrupted payloads")]
    private static void CorruptedProfilesAreRejected()
    {
        var encoded = RuntimeSnippetProfileCodec.Encode(
            KeyinaConfiguration.Default with { FirstRunCompleted = true });
        encoded[^1] ^= 0x5A;
        AssertThrows<InvalidDataException>(() =>
            RuntimeSnippetProfileCodec.Decode(encoded));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"Keyina.RuntimeSnippets.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
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
}
