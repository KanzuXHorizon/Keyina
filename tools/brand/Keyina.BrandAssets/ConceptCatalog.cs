using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Keyina.BrandAssets;

internal static class ConceptCatalog
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static void Generate(string repositoryRoot)
    {
        var imageDirectory = Path.Combine(repositoryRoot, "docs", "image");
        if (!Directory.Exists(imageDirectory))
        {
            throw new DirectoryNotFoundException($"Concept image directory is missing: {imageDirectory}");
        }

        var files = Directory.EnumerateFiles(imageDirectory, "*.png", SearchOption.TopDirectoryOnly)
            .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
            .ToArray();
        if (files.Length != 4)
        {
            throw new InvalidOperationException(
                $"Expected exactly four approved concept PNGs in {imageDirectory}, found {files.Length}.");
        }

        var assets = files.Select(path => CreateAsset(repositoryRoot, path)).ToArray();
        var catalog = new CatalogDocument(1, assets);
        var json = JsonSerializer.Serialize(catalog, SerializerOptions) + "\n";

        var outputDirectory = Path.Combine(repositoryRoot, "docs", "brand");
        Directory.CreateDirectory(outputDirectory);
        AtomicWrite(
            Path.Combine(outputDirectory, "concept-assets.json"),
            Encoding.UTF8.GetBytes(json));
    }

    private static CatalogAsset CreateAsset(string repositoryRoot, string fullPath)
    {
        var bytes = File.ReadAllBytes(fullPath);
        var dimensions = ReadPngDimensions(bytes);
        var relativePath = Path.GetRelativePath(repositoryRoot, fullPath)
            .Replace(Path.DirectorySeparatorChar, '/');
        return new CatalogAsset(
            relativePath,
            Convert.ToHexString(SHA256.HashData(bytes)),
            dimensions.Width,
            dimensions.Height,
            "concept");
    }

    private static (int Width, int Height) ReadPngDimensions(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 24 || !bytes[..8].SequenceEqual(PngSignature))
        {
            throw new InvalidDataException("Concept asset is not a valid PNG.");
        }
        if (!bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            throw new InvalidDataException("PNG does not start with an IHDR chunk.");
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4));
        if (width <= 0 || height <= 0)
        {
            throw new InvalidDataException("PNG dimensions must be positive.");
        }
        return (width, height);
    }

    private static void AtomicWrite(string destination, ReadOnlySpan<byte> content)
    {
        var temporary = destination + ".tmp";
        File.WriteAllBytes(temporary, content.ToArray());
        File.Move(temporary, destination, true);
    }

    private sealed record CatalogDocument(int SchemaVersion, IReadOnlyList<CatalogAsset> Assets);

    private sealed record CatalogAsset(
        string Path,
        string Sha256,
        int Width,
        int Height,
        string Role);
}
