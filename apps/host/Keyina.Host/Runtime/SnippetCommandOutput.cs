using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Keyina.Host.Configuration;
using Keyina.Host.Core.Configuration;
using Keyina.Host.Core.Ipc;

namespace Keyina.Host.Runtime;

public sealed record SnippetCommandRequest(
    int ForegroundProcessId,
    nint FocusWindow,
    SnippetExecutionConfiguration Execution);

public sealed record SnippetCommandOutputResult(
    bool Success,
    string Code,
    string? Output = null);

public static class SnippetCommandRequestStore
{
    private const int HeaderLength = 20;
    private const int MaximumPayloadBytes = 32 * 1024;
    private static ReadOnlySpan<byte> Magic => "KYSC"u8;

    public static string GetRequestDirectory() => Path.Combine(
        Path.GetDirectoryName(ConfigurationPaths.GetProductionPath())!,
        "commands");

    public static SnippetCommandRequest LoadAndDelete(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetFullPath(GetRequestDirectory()) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(directory, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(fullPath), ".bin", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Snippet command request path is outside the runtime directory.");
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(fullPath);
        }
        finally
        {
            try
            {
                File.Delete(fullPath);
            }
            catch (IOException)
            {
                // Best effort cleanup. The commands directory is user-local and requests are unique.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        if (bytes.Length < HeaderLength || bytes.Length > HeaderLength + MaximumPayloadBytes ||
            !bytes.AsSpan(0, 4).SequenceEqual(Magic) ||
            bytes[4] != 1 || bytes[5] != HeaderLength ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6, 2)) != 0)
        {
            throw new InvalidDataException("Snippet command request header is invalid.");
        }

        var processId = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(8, 4));
        var focusWindow = BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(12, 8));
        var payload = bytes.AsSpan(HeaderLength);
        if (processId <= 0 || focusWindow == 0 || payload.Length == 0)
        {
            throw new InvalidDataException("Snippet command request target is invalid.");
        }

        SnippetExecutionConfiguration? execution;
        try
        {
            execution = JsonSerializer.Deserialize<SnippetExecutionConfiguration>(payload);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Snippet command request payload is malformed.", exception);
        }
        if (execution is null)
        {
            throw new InvalidDataException("Snippet command request payload is empty.");
        }
        execution.Validate();
        return new SnippetCommandRequest(processId, (nint)focusWindow, execution);
    }
}

public sealed class SnippetCommandOutputRunner
{
    public const int MaximumOutputCharacters = 16 * 1024;
    public const int MaximumErrorCharacters = 4 * 1024;

    private readonly Func<ProcessStartInfo, Process> startProcess;
    private readonly FocusedUnicodeEnvelopeWriter writer;

    public SnippetCommandOutputRunner(
        FocusedUnicodeEnvelopeWriter? writer = null,
        Func<ProcessStartInfo, Process>? startProcess = null)
    {
        this.writer = writer ?? new FocusedUnicodeEnvelopeWriter();
        this.startProcess = startProcess ?? StartProcess;
    }

    public async Task<SnippetCommandOutputResult> ExecuteAsync(
        SnippetCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!writer.TryBindToExpectedFocus(request.ForegroundProcessId, request.FocusWindow))
        {
            return new SnippetCommandOutputResult(false, "snippet_focus_changed");
        }

        var captured = await CaptureAsync(request.Execution, cancellationToken)
            .ConfigureAwait(false);
        if (!captured.Success || captured.Output is null)
        {
            return captured;
        }

        try
        {
            await writer.WriteAsync(
                    new IpcEnvelope(
                        IpcMessageType.FinalTranscript,
                        Flags: 0,
                        writer.SessionId,
                        writer.FocusGeneration,
                        captured.Output),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (FocusedUnicodeDeliveryException)
        {
            return new SnippetCommandOutputResult(false, "snippet_focus_changed");
        }
        return new SnippetCommandOutputResult(true, "snippet_output_inserted", captured.Output);
    }

    public async Task<SnippetCommandOutputResult> CaptureAsync(
        SnippetExecutionConfiguration execution,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(execution);
        execution.Validate();
        if (!File.Exists(execution.ExecutablePath))
        {
            return new SnippetCommandOutputResult(false, "snippet_executable_missing");
        }
        if (!string.IsNullOrWhiteSpace(execution.WorkingDirectory) &&
            !Directory.Exists(execution.WorkingDirectory))
        {
            return new SnippetCommandOutputResult(false, "snippet_working_directory_missing");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = execution.ExecutablePath,
            Arguments = execution.Arguments ?? string.Empty,
            WorkingDirectory = string.IsNullOrWhiteSpace(execution.WorkingDirectory)
                ? Path.GetDirectoryName(execution.ExecutablePath)!
                : execution.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        Process process;
        try
        {
            process = startProcess(startInfo);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new SnippetCommandOutputResult(false, "snippet_process_start_failed");
        }

        using (process)
        {
            var outputTask = ReadBoundedAsync(
                process.StandardOutput,
                MaximumOutputCharacters,
                cancellationToken);
            var errorTask = ReadBoundedAsync(
                process.StandardError,
                MaximumErrorCharacters,
                cancellationToken);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(execution.TimeoutMilliseconds);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                var output = (await outputTask.ConfigureAwait(false)).TrimEnd('\r', '\n');
                _ = await errorTask.ConfigureAwait(false);
                if (process.ExitCode != 0)
                {
                    return new SnippetCommandOutputResult(false, "snippet_process_failed");
                }
                if (string.IsNullOrEmpty(output))
                {
                    return new SnippetCommandOutputResult(false, "snippet_output_empty");
                }
                return new SnippetCommandOutputResult(true, "snippet_output_captured", output);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKill(process);
                return new SnippetCommandOutputResult(false, "snippet_process_timeout");
            }
            catch (InvalidDataException)
            {
                TryKill(process);
                return new SnippetCommandOutputResult(false, "snippet_output_too_large");
            }
        }
    }

    private static Process StartProcess(ProcessStartInfo startInfo) =>
        Process.Start(startInfo) ?? throw new InvalidOperationException("Process failed to start.");

    private static async Task<string> ReadBoundedAsync(
        StreamReader reader,
        int maximumCharacters,
        CancellationToken cancellationToken)
    {
        var buffer = new char[Math.Min(2_048, maximumCharacters)];
        var result = new StringBuilder();
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                return result.ToString();
            }
            if (result.Length + read > maximumCharacters)
            {
                throw new InvalidDataException("Snippet command output exceeded the configured limit.");
            }
            result.Append(buffer, 0, read);
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }
}
