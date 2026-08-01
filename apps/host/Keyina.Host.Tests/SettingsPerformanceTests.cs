using Keyina.Host.Core.Configuration;
using Keyina.Host.UI;

namespace Keyina.Host.Tests;

internal static class SettingsPerformanceTests
{
    [KeyinaTest("settings defers snippet controls until the snippets section opens")]
    private static void SettingsDefersSnippetControlsUntilNeeded()
    {
        var snippets = CreateSnippets("lazy", 1_000);
        var snapshot = SettingsSnapshot.Sample with
        {
            CustomSnippetCount = snippets.Length,
            Snippets = snippets,
        };
        using var form = new SettingsForm(snapshot, SettingsActions.NoOp);
        var list = (FlowLayoutPanel)form.Controls
            .Find("snippetsList", searchAllChildren: true)
            .Single();

        AssertEx.Equal(0, list.Controls.Count);

        form.OpenSection("snippets");

        var renderedCustomRows = list.Controls
            .Cast<Control>()
            .Count(control => control.Name.StartsWith(
                "snippet_klazy",
                StringComparison.Ordinal));
        AssertEx.Equal(snippets.Length, renderedCustomRows);
    }

    [KeyinaTest("settings reuses snippet controls when only runtime status changes")]
    private static void SettingsReusesSnippetControlsForUnchangedDefinitions()
    {
        var snippets = CreateSnippets("stable", 3);
        var snapshot = SettingsSnapshot.Sample with
        {
            CustomSnippetCount = snippets.Length,
            Snippets = snippets,
        };
        using var form = new SettingsForm(snapshot, SettingsActions.NoOp);
        form.OpenSection("snippets");
        var list = (FlowLayoutPanel)form.Controls
            .Find("snippetsList", searchAllChildren: true)
            .Single();
        var original = list.Controls
            .Cast<Control>()
            .Single(control => string.Equals(
                control.Name,
                "snippet_kstable0000",
                StringComparison.Ordinal));

        form.ApplySnapshot(snapshot with
        {
            Listening = true,
            StatusMessage = "Listening",
        });

        var current = list.Controls
            .Cast<Control>()
            .Single(control => string.Equals(
                control.Name,
                "snippet_kstable0000",
                StringComparison.Ordinal));
        AssertEx.True(
            ReferenceEquals(original, current),
            "Unchanged snippets were rebuilt during a status-only snapshot update.");
    }

    private static SnippetConfiguration[] CreateSnippets(string prefix, int count) =>
        Enumerable.Range(0, count)
            .Select(index => new SnippetConfiguration(
                $";k{prefix}{index:D4}",
                $"Expansion {prefix} {index:D4}",
                CaseSensitive: false,
                PreserveDelimiter: false,
                Delimiters: " ",
                AllowedApplications: [],
                ExcludedApplications: []))
            .ToArray();
}
