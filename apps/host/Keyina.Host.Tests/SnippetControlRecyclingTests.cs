using Keyina.Host.Core.Configuration;
using Keyina.Host.UI;
using Keyina.Host.UI.Fluent;

namespace Keyina.Host.Tests;

internal static class SnippetControlRecyclingTests
{
    private const int LargeSnippetCount = 1_000;

    [KeyinaTest("snippet library recycles custom cards for a complete replacement")]
    private static void RecyclesCustomCardsForCompleteReplacement()
    {
        using var form = new SettingsForm(
            CreateSnapshot("old", "Old expansion"),
            SettingsActions.NoOp);
        form.OpenSection("snippets");

        var before = GetCustomCards(form);
        AssertEx.Equal(LargeSnippetCount, before.Count);
        var beforeSet = before.ToHashSet(ReferenceEqualityComparer.Instance);

        form.ApplySnapshot(CreateSnapshot("new", "New expansion"));

        var after = GetCustomCards(form);
        AssertEx.Equal(LargeSnippetCount, after.Count);
        AssertEx.True(
            after.All(beforeSet.Contains),
            "A complete same-size replacement constructed new custom cards instead of recycling existing controls.");
        AssertEx.True(
            after.Select(GetTriggerText).SequenceEqual(
                Enumerable.Range(0, LargeSnippetCount)
                    .Select(index => $";knew{index:D4}"),
                StringComparer.Ordinal),
            "Recycled cards were not rebound to the replacement triggers in the requested order.");
    }

    [KeyinaTest("snippet library updates same trigger content without replacing controls")]
    private static void UpdatesSameTriggerContentWithoutReplacingControls()
    {
        using var form = new SettingsForm(
            CreateSnapshot("stable", "Before"),
            SettingsActions.NoOp);
        form.OpenSection("snippets");

        var before = GetCustomCards(form);
        form.ApplySnapshot(CreateSnapshot("stable", "After"));
        var after = GetCustomCards(form);

        AssertEx.Equal(before.Count, after.Count);
        for (var index = 0; index < before.Count; index++)
        {
            AssertEx.True(
                ReferenceEquals(before[index], after[index]),
                $"Card {index} was replaced even though its trigger was unchanged.");
        }
        AssertEx.True(
            after.All(card => GetExpansionText(card).StartsWith(
                "After",
                StringComparison.Ordinal)),
            "Existing cards did not display the updated expansion text.");
    }

    [KeyinaTest("snippet library creates and disposes only the row-count delta")]
    private static void CreatesAndDisposesOnlyTheRowCountDelta()
    {
        using var form = new SettingsForm(
            CreateSnapshot("delta", "Initial", count: 100),
            SettingsActions.NoOp);
        form.OpenSection("snippets");

        var initial = GetCustomCards(form);
        var initialSet = initial.ToHashSet(ReferenceEqualityComparer.Instance);
        form.ApplySnapshot(CreateSnapshot("delta", "Expanded", count: 101));
        var expanded = GetCustomCards(form);

        AssertEx.Equal(101, expanded.Count);
        AssertEx.True(
            expanded.Take(100).SequenceEqual(initial, ReferenceEqualityComparer.Instance),
            "Adding one snippet replaced existing cards.");
        AssertEx.False(
            initialSet.Contains(expanded[^1]),
            "Adding one snippet did not create exactly one new card.");

        var surplus = expanded.Skip(99).ToArray();
        form.ApplySnapshot(CreateSnapshot("delta", "Trimmed", count: 99));
        var trimmed = GetCustomCards(form);

        AssertEx.Equal(99, trimmed.Count);
        AssertEx.True(
            trimmed.SequenceEqual(initial.Take(99), ReferenceEqualityComparer.Instance),
            "Removing snippets replaced retained cards.");
        AssertEx.True(
            surplus.All(card => card.IsDisposed),
            "Surplus cards were removed without being disposed.");
    }

    [KeyinaTest("snippet library preserves active filtering after recycling")]
    private static void PreservesActiveFilteringAfterRecycling()
    {
        using var form = new SettingsForm(
            CreateSnapshot("before", "Ordinary", count: 3),
            SettingsActions.NoOp)
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(-10_000, -10_000),
            Opacity = 0,
        };
        form.Show();
        form.OpenSection("snippets");
        var search = (TextBox)form.Controls.Find("snippetsSearch", true).Single();
        search.Text = "needle";
        Application.DoEvents();

        var replacement = CreateSnapshot("after", "Ordinary", count: 3);
        replacement = replacement with
        {
            Snippets = replacement.Snippets
                .Select((snippet, index) => index == 1
                    ? snippet with { Expansion = "Contains needle after recycling" }
                    : snippet)
                .ToArray(),
        };
        form.ApplySnapshot(replacement);
        Application.DoEvents();

        var cards = GetCustomCards(form);
        AssertEx.Equal(1, cards.Count(card => card.Visible));
        AssertEx.Equal(";kafter0001", GetTriggerText(cards.Single(card => card.Visible)));
    }

    [KeyinaTest("snippet library keeps stable controls across repeated replacements")]
    private static void KeepsStableControlsAcrossRepeatedReplacements()
    {
        using var form = new SettingsForm(
            CreateSnapshot("cyclea", "Cycle A", count: 250),
            SettingsActions.NoOp);
        form.OpenSection("snippets");
        var initial = GetCustomCards(form);
        var initialSet = initial.ToHashSet(ReferenceEqualityComparer.Instance);

        for (var iteration = 0; iteration < 20; iteration++)
        {
            var useA = (iteration & 1) == 0;
            form.ApplySnapshot(CreateSnapshot(
                useA ? "cycleb" : "cyclea",
                useA ? "Cycle B" : "Cycle A",
                count: 250));
            var current = GetCustomCards(form);
            AssertEx.Equal(250, current.Count);
            AssertEx.True(
                current.All(initialSet.Contains),
                $"Replacement {iteration} introduced a new custom card.");
            AssertEx.False(
                current.Any(card => card.IsDisposed),
                $"Replacement {iteration} retained a disposed card.");
        }
    }

    [KeyinaTest("snippet action controls resolve the recycled row")]
    private static void ActionControlsResolveTheRecycledRow()
    {
        using var form = new SettingsForm(
            CreateSnapshot("oldaction", "Before", count: 1),
            SettingsActions.NoOp);
        form.OpenSection("snippets");
        form.ApplySnapshot(CreateSnapshot("newaction", "After", count: 1));

        var card = GetCustomCards(form).Single();
        var action = card.Controls
            .OfType<TableLayoutPanel>()
            .Single()
            .GetControlFromPosition(3, 0)!;
        var resolver = typeof(SettingsForm).GetMethod(
            "ResolveSnippetRow",
            System.Reflection.BindingFlags.Static |
            System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Snippet action resolver was not found.");
        var row = resolver.Invoke(null, [action])
            ?? throw new InvalidOperationException("Snippet action did not resolve a row.");
        var trigger = row.GetType().GetProperty("Trigger")?.GetValue(row) as string;

        AssertEx.Equal(";knewaction0000", trigger);
    }

    private static SettingsSnapshot CreateSnapshot(
        string prefix,
        string expansionPrefix,
        int count = LargeSnippetCount)
    {
        var snippets = Enumerable.Range(0, count)
            .Select(index => new SnippetConfiguration(
                $";k{prefix}{index:D4}",
                $"{expansionPrefix} {index:D4}",
                CaseSensitive: false,
                PreserveDelimiter: false,
                Delimiters: " ",
                AllowedApplications: [],
                ExcludedApplications: []))
            .ToArray();
        return SettingsSnapshot.Sample with
        {
            CustomSnippetCount = snippets.Length,
            Snippets = snippets,
        };
    }

    private static List<FluentCard> GetCustomCards(SettingsForm form)
    {
        var list = (FlowLayoutPanel)form.Controls
            .Find("snippetsList", searchAllChildren: true)
            .Single();
        return list.Controls
            .OfType<FluentCard>()
            .Where(card => card.Controls
                .OfType<TableLayoutPanel>()
                .Single()
                .ColumnCount == 6)
            .ToList();
    }

    private static string GetTriggerText(FluentCard card) =>
        GetRowLabels(card)[0].Text;

    private static string GetExpansionText(FluentCard card) =>
        GetRowLabels(card)[1].Text;

    private static Label[] GetRowLabels(FluentCard card) =>
        card.Controls
            .OfType<TableLayoutPanel>()
            .Single()
            .Controls
            .OfType<Label>()
            .OrderBy(label => label.TabIndex)
            .Take(3)
            .ToArray();
}
