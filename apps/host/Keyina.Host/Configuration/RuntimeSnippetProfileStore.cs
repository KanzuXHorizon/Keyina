using Keyina.Host.Core.Configuration;

namespace Keyina.Host.Configuration;

public sealed class RuntimeSnippetProfileException : Exception
{
    public RuntimeSnippetProfileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class RuntimeSnippetProfileStore
{
    private readonly string path;
    private readonly string temporaryPath;

    public RuntimeSnippetProfileStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException(
                "Runtime snippet profile path must be fully qualified.",
                nameof(path));
        }

        this.path = path;
        temporaryPath = path + ".tmp";
    }

    public static RuntimeSnippetProfileStore CreateProduction() =>
        new(ConfigurationPaths.GetRuntimeSnippetProfilePath());

    public async Task PublishAsync(
        KeyinaConfiguration configuration,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        try
        {
            var bytes = RuntimeSnippetProfileCodec.Encode(configuration);
            var directory = Path.GetDirectoryName(path)
                ?? throw new IOException(
                    "Runtime snippet profile path has no parent directory.");
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
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
            ConfigurationValidationException)
        {
            throw new RuntimeSnippetProfileException(
                "Runtime snippet profile could not be published atomically.",
                exception);
        }
    }
}
