using System.Drawing;
using System.Security.Cryptography;
using System.Text.Json;

namespace Keyina.Host.Tests;

internal static class GeneratedBrandAssetTests
{
    private static readonly int[] AppSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256, 512];
    private static readonly int[] IconFrameSizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
    private static readonly string[] TrayStates = ["active", "inactive", "listening"];

    [KeyinaTest("generated brand manifest matches all PNG and ICO assets")]
    private static void GeneratedAssetsMatchManifest()
    {
        var manifestPath = Path.Combine(
            RepositoryPaths.Root, "brand", "generated", "manifest.json");
        AssertEx.True(File.Exists(manifestPath), $"Missing generated brand manifest: {manifestPath}");

        using var document = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
        var root = document.RootElement;
        AssertEx.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        var assets = root.GetProperty("assets").EnumerateArray().ToArray();
        AssertEx.Equal(42, assets.Length, "Unexpected generated brand asset count.");

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in assets)
        {
            var relativePath = asset.GetProperty("path").GetString();
            AssertEx.NotNull(relativePath, "Generated asset path must not be null.");
            AssertEx.True(seen.Add(relativePath!), $"Duplicate generated asset: {relativePath}");

            var fullPath = Path.Combine(
                RepositoryPaths.Root,
                relativePath!.Replace('/', Path.DirectorySeparatorChar));
            AssertEx.True(File.Exists(fullPath), $"Generated asset is missing: {relativePath}");
            var bytes = File.ReadAllBytes(fullPath);
            AssertEx.Equal(
                asset.GetProperty("sha256").GetString(),
                Convert.ToHexString(SHA256.HashData(bytes)),
                $"Generated asset hash mismatch: {relativePath}");

            var format = asset.GetProperty("format").GetString();
            if (format == "png")
            {
                using var bitmap = new Bitmap(fullPath);
                AssertEx.Equal(asset.GetProperty("width").GetInt32(), bitmap.Width);
                AssertEx.Equal(asset.GetProperty("height").GetInt32(), bitmap.Height);
                AssertPngHasVisibleAndTransparentPixels(bitmap, relativePath);
            }
            else if (format == "ico")
            {
                ValidateIco(bytes, relativePath, asset.GetProperty("frames"));
            }
            else
            {
                throw new InvalidOperationException($"Unsupported manifest format: {format}");
            }
        }

        AssertExpectedPaths(seen);
    }

    private static void AssertExpectedPaths(HashSet<string> paths)
    {
        foreach (var size in AppSizes)
        {
            AssertEx.True(
                paths.Contains($"brand/generated/app/keyina-app-{size}.png"),
                $"Missing app PNG size {size}.");
        }

        foreach (var state in TrayStates)
        {
            foreach (var size in IconFrameSizes)
            {
                AssertEx.True(
                    paths.Contains($"brand/generated/tray/keyina-tray-{state}-{size}.png"),
                    $"Missing tray {state} PNG size {size}.");
            }
            AssertEx.True(
                paths.Contains($"brand/generated/keyina-tray-{state}.ico"),
                $"Missing tray {state} ICO.");
        }

        AssertEx.True(paths.Contains("brand/generated/keyina.ico"), "Missing application ICO.");
        AssertEx.True(
            paths.Contains("brand/generated/lockup/keyina-lockup-1680x512.png"),
            "Missing lockup PNG.");
    }

    private static void AssertPngHasVisibleAndTransparentPixels(Bitmap bitmap, string asset)
    {
        var hasVisible = false;
        var hasTransparent = false;
        var stepX = Math.Max(1, bitmap.Width / 32);
        var stepY = Math.Max(1, bitmap.Height / 32);
        for (var y = 0; y < bitmap.Height; y += stepY)
        {
            for (var x = 0; x < bitmap.Width; x += stepX)
            {
                var alpha = bitmap.GetPixel(x, y).A;
                hasVisible |= alpha > 0;
                hasTransparent |= alpha < 255;
            }
        }

        AssertEx.True(hasVisible, $"PNG is fully transparent: {asset}");
        AssertEx.True(hasTransparent, $"PNG has no transparent safe area: {asset}");
    }

    private static void ValidateIco(
        ReadOnlySpan<byte> bytes,
        string asset,
        JsonElement manifestFrames)
    {
        AssertEx.True(bytes.Length >= 6, $"ICO is too short: {asset}");
        AssertEx.Equal((ushort)0, BitConverter.ToUInt16(bytes[..2]));
        AssertEx.Equal((ushort)1, BitConverter.ToUInt16(bytes.Slice(2, 2)));
        var count = BitConverter.ToUInt16(bytes.Slice(4, 2));
        AssertEx.Equal(IconFrameSizes.Length, (int)count, $"Unexpected ICO frame count: {asset}");

        var expectedFrames = manifestFrames.EnumerateArray().Select(frame => frame.GetInt32()).ToArray();
        AssertEx.True(expectedFrames.SequenceEqual(IconFrameSizes), $"Manifest ICO frames are not stable: {asset}");

        var actualFrames = new List<int>(count);
        for (var index = 0; index < count; index++)
        {
            var entry = bytes.Slice(6 + (index * 16), 16);
            var width = entry[0] == 0 ? 256 : entry[0];
            var height = entry[1] == 0 ? 256 : entry[1];
            AssertEx.Equal(width, height, $"ICO frame is not square: {asset}");
            actualFrames.Add(width);

            var length = BitConverter.ToInt32(entry.Slice(8, 4));
            var offset = BitConverter.ToInt32(entry.Slice(12, 4));
            AssertEx.True(length > 8 && offset >= 6 + (count * 16) && offset + length <= bytes.Length,
                $"ICO frame bounds are invalid: {asset}");
            ReadOnlySpan<byte> pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
            AssertEx.True(bytes.Slice(offset, 8).SequenceEqual(pngSignature),
                $"ICO frame is not PNG-compressed: {asset}");
        }

        AssertEx.True(actualFrames.SequenceEqual(IconFrameSizes), $"ICO frame order is unstable: {asset}");
    }
}
