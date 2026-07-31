using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Keyina.Host.Core.Configuration;
using Keyina.Host.Core.Snippets;
using Keyina.Host.Runtime;
using Keyina.Host.UI;

namespace Keyina.Host.Tests;

internal static class SnippetCommandOutputTests
{
    [KeyinaTest("command-output snippet serializes a validated executable payload")]
    private static void CommandConfigurationCreatesExternalDefinition()
    {
        var execution = new SnippetExecutionConfiguration(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            "/d /c echo hello",
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            2_000);
        var configuration = new SnippetConfiguration(
            ";khello",
            string.Empty,
            false,
            false,
            " ",
            [],
            [],
            execution);

        var definition = configuration.ToDefinition();

        AssertEx.Equal(SnippetCommand.ExternalOutput, definition.Command);
        var decoded = JsonSerializer.Deserialize<SnippetExecutionConfiguration>(definition.Expansion);
        AssertEx.NotNull(decoded, "External command payload did not deserialize.");
        AssertEx.Equal(execution.ExecutablePath, decoded!.ExecutablePath);
        AssertEx.Equal(execution.Arguments, decoded.Arguments);
    }

    [KeyinaTest("command-output runner captures bounded stdout without a console window")]
    private static void CommandRunnerCapturesStdout()
    {
        var command = new SnippetExecutionConfiguration(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            "/d /c echo Keyina-command-output",
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            2_000);

        var result = new SnippetCommandOutputRunner()
            .CaptureAsync(command, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        AssertEx.True(result.Success, $"Command capture failed: {result.Code}.");
        AssertEx.Equal("Keyina-command-output", result.Output);
    }

    [KeyinaTest("snippet command request store accepts only user-local binary requests")]
    private static void RequestStoreReadsAndDeletesValidRequest()
    {
        var directory = SnippetCommandRequestStore.GetRequestDirectory();
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"test-{Guid.NewGuid():N}.bin");
        var execution = new SnippetExecutionConfiguration(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"),
            "/d /c echo request",
            string.Empty,
            1_000);
        var payload = JsonSerializer.SerializeToUtf8Bytes(execution);
        var bytes = new byte[20 + payload.Length];
        "KYSC"u8.CopyTo(bytes);
        bytes[4] = 1;
        bytes[5] = 20;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8, 4), 1234);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(12, 8), 5678);
        payload.CopyTo(bytes.AsSpan(20));
        File.WriteAllBytes(path, bytes);

        var request = SnippetCommandRequestStore.LoadAndDelete(path);

        AssertEx.Equal(1234, request.ForegroundProcessId);
        AssertEx.Equal((nint)5678, request.FocusWindow);
        AssertEx.Equal(execution.Arguments, request.Execution.Arguments);
        AssertEx.True(!File.Exists(path), "Consumed request file was not deleted.");
    }

    [KeyinaTest("snippet editor exposes executable arguments working directory timeout and preview")]
    private static void EditorExposesFriendlyCommandFields()
    {
        using var form = new SnippetEditorDialog(null, Array.Empty<string>());

        AssertEx.NotNull(form.Controls.Find("snippetKind", true).SingleOrDefault(), "Snippet type selector is missing.");
        AssertEx.NotNull(form.Controls.Find("snippetExecutablePath", true).SingleOrDefault(), "Executable path field is missing.");
        AssertEx.NotNull(form.Controls.Find("browseSnippetExecutable", true).SingleOrDefault(), "Executable browser is missing.");
        AssertEx.NotNull(form.Controls.Find("snippetArguments", true).SingleOrDefault(), "Arguments field is missing.");
        AssertEx.NotNull(form.Controls.Find("snippetWorkingDirectory", true).SingleOrDefault(), "Working directory field is missing.");
        AssertEx.NotNull(form.Controls.Find("snippetTimeout", true).SingleOrDefault(), "Timeout field is missing.");
        AssertEx.NotNull(form.Controls.Find("previewSnippetCommand", true).SingleOrDefault(), "Command preview action is missing.");
        AssertEx.NotNull(form.Controls.Find("usePowerShell", true).SingleOrDefault(), "PowerShell preset is missing.");
    }
}
