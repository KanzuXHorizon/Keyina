using System.Text.Json;
using System.Text.Json.Serialization;
using Keyina.Host.Core.Configuration;

namespace Keyina.Host.Configuration;

public static class PortableSettingsService
{
    private const int MaximumPortableSettingsBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static async Task ExportAsync(
        string path,
        KeyinaConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ValidatePath(path);
        ArgumentNullException.ThrowIfNull(configuration);
        _ = configuration.ValidateAndCreateSnippets();
        var document = new PortableSettingsDocument(
            PortableSettingsDocument.CurrentFormatVersion,
            configuration);
        document.Validate();

        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        if (bytes.Length > MaximumPortableSettingsBytes)
        {
            throw new ConfigurationException(
                $"Portable settings exceed {MaximumPortableSettingsBytes} bytes.");
        }

        var directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException(
                "Portable settings path must have a parent directory.",
                nameof(path));
        Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken)
                    .ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken)
                    .ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(path))
            {
                File.Replace(temporaryPath, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new ConfigurationException(
                "Portable settings could not be exported safely.",
                exception);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static async Task<KeyinaConfiguration> ImportAsync(
        string path,
        CancellationToken cancellationToken)
    {
        ValidatePath(path);
        try
        {
            var information = new FileInfo(path);
            if (!information.Exists)
            {
                throw new ConfigurationException(
                    "Portable settings file does not exist.");
            }
            if (information.Length > MaximumPortableSettingsBytes)
            {
                throw new ConfigurationException(
                    $"Portable settings exceed {MaximumPortableSettingsBytes} bytes.");
            }

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var document = await JsonSerializer.DeserializeAsync<PortableSettingsDocument>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? throw new ConfigurationException(
                    "Portable settings JSON was empty.");
            document.Validate();
            return document.Configuration;
        }
        catch (ConfigurationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or
            ConfigurationValidationException)
        {
            throw new ConfigurationException(
                "Portable settings could not be imported safely.",
                exception);
        }
    }

    private static void ValidatePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException(
                "Portable settings path must be fully qualified.",
                nameof(path));
        }
        if (!string.Equals(
                Path.GetExtension(path),
                ".json",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Portable settings must use a .json file.",
                nameof(path));
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            ReadCommentHandling = JsonCommentHandling.Disallow,
            AllowTrailingCommas = false,
            WriteIndented = true,
        };
        options.Converters.Add(new JsonStringEnumConverter(
            JsonNamingPolicy.SnakeCaseLower,
            allowIntegerValues: false));
        return options;
    }
}
