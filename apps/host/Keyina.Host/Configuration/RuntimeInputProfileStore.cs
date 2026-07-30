using Keyina.Host.Core.Configuration;

namespace Keyina.Host.Configuration;

public sealed class RuntimeInputProfileException : Exception
{
    public RuntimeInputProfileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class RuntimeInputProfileStore
{
    private readonly string path;
    private readonly string temporaryPath;

    public RuntimeInputProfileStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException(
                "Runtime input profile path must be fully qualified.",
                nameof(path));
        }

        this.path = path;
        temporaryPath = path + ".tmp";
    }

    public static RuntimeInputProfileStore CreateProduction() =>
        new(ConfigurationPaths.GetRuntimeInputProfilePath());

    public async Task PublishAsync(
        KeyinaConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        try
        {
            var bytes = RuntimeInputProfileCodec.Encode(configuration);
            var directory = Path.GetDirectoryName(path)
                ?? throw new IOException(
                    "Runtime input profile path has no parent directory.");
            Directory.CreateDirectory(directory);

            try
            {
                await using (var stream = new FileStream(
                    temporaryPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: RuntimeInputProfileCodec.EncodedLength,
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            ConfigurationValidationException)
        {
            throw new RuntimeInputProfileException(
                "Runtime input profile could not be published atomically.",
                exception);
        }
    }
}
