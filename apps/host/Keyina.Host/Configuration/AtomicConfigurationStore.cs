using System.Text.Json;
using System.Text.Json.Serialization;
using Keyina.Host.Core.Configuration;

namespace Keyina.Host.Configuration;

public static class ConfigurationPaths
{
    public static string GetProductionPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Keyina",
        "settings.json");
}

public sealed class ConfigurationException : Exception
{
    public ConfigurationException(string message)
        : base(message)
    {
    }

    public ConfigurationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class AtomicConfigurationStore
{
    private const int MaximumConfigurationBytes = 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly string path;
    private readonly string temporaryPath;

    public AtomicConfigurationStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException(
                "Configuration path must be fully qualified.",
                nameof(path));
        }

        this.path = path;
        temporaryPath = path + ".tmp";
    }

    public static AtomicConfigurationStore CreateProduction() =>
        new(ConfigurationPaths.GetProductionPath());

    public async Task<KeyinaConfiguration> LoadAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return KeyinaConfiguration.Default;
        }

        try
        {
            var information = new FileInfo(path);
            if (information.Length > MaximumConfigurationBytes)
            {
                throw new ConfigurationException(
                    $"Configuration exceeds {MaximumConfigurationBytes} bytes.");
            }

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var configuration = await JsonSerializer.DeserializeAsync<KeyinaConfiguration>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? throw new ConfigurationException("Configuration JSON was empty.");
            _ = configuration.ValidateAndCreateSnippets();
            return configuration;
        }
        catch (ConfigurationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException or
            ConfigurationValidationException)
        {
            throw new ConfigurationException(
                "Configuration could not be loaded safely.",
                exception);
        }
    }

    public async Task SaveAsync(
        KeyinaConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        try
        {
            _ = configuration.ValidateAndCreateSnippets();
            var bytes = JsonSerializer.SerializeToUtf8Bytes(configuration, JsonOptions);
            if (bytes.Length > MaximumConfigurationBytes)
            {
                throw new ConfigurationException(
                    $"Configuration exceeds {MaximumConfigurationBytes} bytes.");
            }

            var directory = Path.GetDirectoryName(path)
                ?? throw new ConfigurationException(
                    "Configuration path does not contain a parent directory.");
            Directory.CreateDirectory(directory);

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
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        catch (ConfigurationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            ConfigurationValidationException or JsonException)
        {
            throw new ConfigurationException(
                "Configuration could not be saved atomically.",
                exception);
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
