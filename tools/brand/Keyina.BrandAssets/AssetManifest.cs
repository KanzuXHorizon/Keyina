using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Keyina.BrandAssets;

internal static class AssetManifest
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static GeneratedAsset Create(
        string repositoryRoot,
        string fullPath,
        string role,
        string format,
        int width,
        int height,
        string sourceRelativePath,
        int[]? frames = null)
    {
        var relativePath = Normalize(Path.GetRelativePath(repositoryRoot, fullPath));
        var sourceFullPath = Path.Combine(
            repositoryRoot,
            sourceRelativePath.Replace('/', Path.DirectorySeparatorChar));
        return new GeneratedAsset(
            relativePath,
            role,
            format,
            width,
            height,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(fullPath))),
            sourceRelativePath,
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sourceFullPath))),
            frames);
    }

    public static void Write(string repositoryRoot, IReadOnlyList<GeneratedAsset> assets)
    {
        var ordered = assets.OrderBy(asset => asset.Path, StringComparer.Ordinal).ToArray();
        var document = new GeneratedAssetDocument(1, ordered);
        var json = JsonSerializer.Serialize(document, SerializerOptions) + "\n";
        var destination = Path.Combine(repositoryRoot, "brand", "generated", "manifest.json");
        AtomicWrite(destination, Encoding.UTF8.GetBytes(json));
    }

    private static string Normalize(string path) =>
        path.Replace(Path.DirectorySeparatorChar, '/');

    private static void AtomicWrite(string destination, ReadOnlySpan<byte> content)
    {
        var temporary = destination + ".tmp";
        File.WriteAllBytes(temporary, content.ToArray());
        File.Move(temporary, destination, true);
    }

    private sealed record GeneratedAssetDocument(
        int SchemaVersion,
        IReadOnlyList<GeneratedAsset> Assets);
}

internal sealed record GeneratedAsset(
    string Path,
    string Role,
    string Format,
    int Width,
    int Height,
    string Sha256,
    string Source,
    string SourceSha256,
    int[]? Frames);
