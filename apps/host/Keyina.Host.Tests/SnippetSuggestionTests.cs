using Keyina.Host.Core.Snippets;
using Keyina.Host.UI;

namespace Keyina.Host.Tests;

internal static class SnippetSuggestionTests
{
    [KeyinaTest("snippet suggestions start only after the ;k prefix and remain bounded")]
    private static void SuggestionsRequireStandardPrefix()
    {
        var session = new SnippetSuggestionSession();
        session.UpdateDefinitions(BuiltInSnippets.Create());

        AssertEx.Equal(0, session.Push(';').Count);
        var suggestions = session.Push('k');

        AssertEx.True(suggestions.Count > 0, ";k did not show built-in snippets.");
        AssertEx.True(suggestions.Count <= 8, "Suggestion list exceeded the visible bound.");
        AssertEx.True(
            suggestions.All(item => item.Trigger.StartsWith(";k", StringComparison.OrdinalIgnoreCase)),
            "Suggestion list contained a non-;k trigger.");
    }

    [KeyinaTest("snippet suggestion session supports filtering backspace and boundary reset")]
    private static void SuggestionsTrackTypedPrefix()
    {
        var session = new SnippetSuggestionSession();
        session.UpdateDefinitions(BuiltInSnippets.Create());

        _ = session.Push(';');
        _ = session.Push('k');
        var voice = session.Push('v');
        AssertEx.True(
            voice.All(item => item.Trigger.StartsWith(";kv", StringComparison.OrdinalIgnoreCase)),
            "Typed prefix did not filter suggestions.");

        var all = session.Push('\b');
        AssertEx.True(all.Count >= voice.Count, "Backspace did not widen the result set.");
        AssertEx.Equal(0, session.Push(' ').Count);
        AssertEx.Equal(string.Empty, session.Prefix);
    }

    [KeyinaTest("snippet suggestion overlay is topmost non activating and scrollable")]
    private static void OverlayContractIsNonIntrusive()
    {
        using var form = new SnippetSuggestionOverlayForm();
        var rows = form.Controls.Find("snippetSuggestionRows", true)
            .OfType<FlowLayoutPanel>()
            .Single();

        AssertEx.True(form.TopMost, "Suggestion overlay was not topmost.");
        AssertEx.False(form.ShowInTaskbar, "Suggestion overlay appeared in the taskbar.");
        AssertEx.True(form.UsesNoActivateStyle, "Suggestion overlay could activate and steal focus.");
        AssertEx.True(rows.AutoScroll, "Suggestion overlay rows were not scrollable.");
    }

    [KeyinaTest("snippet settings library scrolls and exposes create action")]
    private static void SettingsLibrarySupportsManagement()
    {
        using var form = new SettingsForm(SettingsSnapshot.Sample, SettingsActions.NoOp);
        form.OpenSection("snippets");
        var list = form.Controls.Find("snippetsList", true)
            .OfType<FlowLayoutPanel>()
            .Single();
        var add = form.Controls.Find("addSnippet", true)
            .OfType<Button>()
            .Single();

        AssertEx.True(list.AutoScroll, "Snippet library was not scrollable.");
        AssertEx.Equal("Thêm gõ tắt", add.Text);
        AssertEx.True(list.Controls.Count >= 8, "Built-in and sample custom snippets were not rendered.");
    }
}
