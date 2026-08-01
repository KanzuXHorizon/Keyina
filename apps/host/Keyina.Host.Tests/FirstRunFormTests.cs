using System.Reflection;
using Keyina.Host.UI;

namespace Keyina.Host.Tests;

[KeyinaInteractiveTest]
internal static class FirstRunFormTests
{
    [KeyinaTest("first-run form exposes optional setup paths and completion controls")]
    private static void FirstRunStructureIsComplete()
    {
        var openedSections = new List<string>();
        var completed = 0;
        using var form = new FirstRunForm(
            openedSections.Add,
            () => completed++);

        AssertEx.Equal("Bắt đầu với Keyina", form.Text);
        AssertEx.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
        AssertEx.True(
            form.AccessibleDescription?.Contains("không bắt buộc", StringComparison.OrdinalIgnoreCase) == true,
            "First-run accessibility copy did not explain optional setup.");
        foreach (var name in new[]
                 {
                     "firstRunTyping",
                     "firstRunTypingInput",
                     "firstRunTypingState",
                     "firstRunSpeech",
                     "firstRunSpeechBadge",
                     "firstRunTranslation",
                     "firstRunTranslationBadge",
                     "completeFirstRun",
                     "skipFirstRun",
                 })
        {
            AssertEx.Equal(1, form.Controls.Find(name, true).Length);
        }

        var typingInput = (TextBox)form.Controls.Find("firstRunTypingInput", true).Single();
        var typingState = (Label)form.Controls.Find("firstRunTypingState", true).Single();
        AssertEx.True(typingInput.TabIndex <= ((Button)form.Controls.Find("firstRunSpeech", true).Single()).TabIndex,
            "Typing verification should appear before optional setup actions.");
        typingInput.Text = "tiếng Việt";
        AssertEx.Equal("Đã gõ thử", typingState.Text);
        AssertEx.True(
            ((Label)form.Controls.Find("firstRunSpeechBadge", true).Single()).Text.Contains("Tùy chọn", StringComparison.Ordinal),
            "Speech setup was not marked optional.");

        InvokeClick((Button)form.Controls.Find("firstRunSpeech", true).Single());
        AssertEx.True(openedSections.SequenceEqual(["speech"]),
            "Speech setup did not open the expected settings section.");
        AssertEx.Equal(0, completed);
    }

    [KeyinaTest("first-run completion and skip both persist completion exactly once")]
    private static void CompletionActionsAreIdempotent()
    {
        foreach (var buttonName in new[] { "completeFirstRun", "skipFirstRun" })
        {
            var completed = 0;
            using var form = new FirstRunForm(
                _ => { },
                () => completed++);
            var button = (Button)form.Controls.Find(buttonName, true).Single();

            InvokeClick(button);
            InvokeClick(button);

            AssertEx.Equal(1, completed);
        }
    }

    private static void InvokeClick(Button button)
    {
        var method = button.GetType().GetMethod(
            "OnClick",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Button click handler was not found.");
        _ = method.Invoke(button, [EventArgs.Empty]);
    }
}
