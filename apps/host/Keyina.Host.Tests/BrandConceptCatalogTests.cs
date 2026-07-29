using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace Keyina.Host.Tests;

internal static class BrandConceptCatalogTests
{
    [KeyinaTest("brand concept catalog matches exactly four approved PNG files")]
    private static void CatalogMatchesApprovedConcepts()
    {
        var catalogPath = Path.Combine(
            RepositoryPaths.Root, "docs", "brand", "concept-assets.json");
        AssertEx.True(File.Exists(catalogPath), $"Missing brand catalog: {catalogPath}");

        using var document = JsonDocument.Parse(File.ReadAllBytes(catalogPath));
        var root = document.RootElement;
        AssertEx.Equal(1, root.GetProperty("schemaVersion").GetInt32());

        var assets = root.GetProperty("assets").EnumerateArray().ToArray();
        AssertEx.Equal(4, assets.Length, "The approved concept set must contain exactly four files.");

        var uniquePaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in assets)
        {
            var relativePath = asset.GetProperty("path").GetString();
            AssertEx.NotNull(relativePath, "Concept path must not be null.");
            AssertEx.True(uniquePaths.Add(relativePath!), $"Duplicate catalog path: {relativePath}");
            AssertEx.Equal("concept", asset.GetProperty("role").GetString());

            var fullPath = Path.Combine(
                RepositoryPaths.Root,
                relativePath!.Replace('/', Path.DirectorySeparatorChar));
            AssertEx.True(File.Exists(fullPath), $"Catalogued concept is missing: {relativePath}");

            var bytes = File.ReadAllBytes(fullPath);
            var actualHash = Convert.ToHexString(SHA256.HashData(bytes));
            AssertEx.Equal(asset.GetProperty("sha256").GetString(), actualHash,
                $"SHA-256 mismatch for {relativePath}");

            var dimensions = ReadPngDimensions(bytes);
            AssertEx.Equal(1536, dimensions.Width, $"Unexpected width for {relativePath}");
            AssertEx.Equal(1024, dimensions.Height, $"Unexpected height for {relativePath}");
            AssertEx.Equal(dimensions.Width, asset.GetProperty("width").GetInt32());
            AssertEx.Equal(dimensions.Height, asset.GetProperty("height").GetInt32());
        }
    }

    private static (int Width, int Height) ReadPngDimensions(ReadOnlySpan<byte> bytes)
    {
        ReadOnlySpan<byte> signature = [137, 80, 78, 71, 13, 10, 26, 10];
        AssertEx.True(bytes.Length >= 24, "PNG is too short to contain an IHDR chunk.");
        AssertEx.True(bytes[..8].SequenceEqual(signature), "File does not have a PNG signature.");
        AssertEx.True(bytes.Slice(12, 4).SequenceEqual("IHDR"u8), "PNG does not begin with IHDR.");

        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4));
        AssertEx.True(width > 0 && height > 0, "PNG dimensions must be positive.");
        return (width, height);
    }
}
