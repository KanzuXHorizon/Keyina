using Keyina.Host.Core.Snippets;
using Keyina.Host.UI;

namespace Keyina.Host.Tests;

internal static class SnippetSuggestionOverlayFormTests
{
    [KeyinaTest("snippet overlay bounds visible suggestions and exposes selection metadata")]
    private static void OverlayBoundsAndDescribesSuggestions()
    {
        using var form = new SnippetSuggestionOverlayForm();
        var suggestions = Enumerable.Range(1, 9)
            .Select(index => CreateSnippet($";m{index}", $"Mẫu nội dung {index}"))
            .ToArray();

        form.Present(";m", suggestions);

        AssertEx.True(form.UsesNoActivateStyle, "Snippet overlay could steal focus.");
        AssertEx.Equal(SnippetSuggestionOverlayForm.MaximumVisibleSuggestions,
            form.Controls.Find("snippetSuggestionRows", true).Single().Controls.Count);
        var first = form.Controls.Find("snippetSuggestionRow0", true).Single();
        AssertEx.True(first.AccessibleDescription?.Contains("đang được chọn", StringComparison.OrdinalIgnoreCase) == true,
            "First suggestion did not expose selected-state metadata.");
        AssertEx.Equal(1, form.Controls.Find("snippetSuggestionTrigger0", true).Length);
        AssertEx.True(form.Height <= 314, "Suggestion overlay exceeded its bounded height.");
    }

    private static SnippetDefinition CreateSnippet(string trigger, string expansion) => new(
        trigger,
        expansion,
        CaseSensitive: false,
        PreserveDelimiter: false,
        Delimiters: new HashSet<char> { ' ', '\n' },
        AllowedApplications: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
        ExcludedApplications: new HashSet<string>(StringComparer.OrdinalIgnoreCase));
}
