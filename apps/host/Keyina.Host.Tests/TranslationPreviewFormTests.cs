using System.Reflection;
using Keyina.Host.Translation;
using Keyina.Host.UI;

namespace Keyina.Host.Tests;

internal static class TranslationPreviewFormTests
{
    [KeyinaTest("translation preview form exposes original translated and explicit actions")]
    private static void PreviewStructureIsComplete()
    {
        var replaced = 0;
        var copied = string.Empty;
        var cancelled = 0;
        var preview = CreatePreview();
        using var form = new TranslationPreviewForm(
            preview,
            _ => replaced++,
            text => copied = text,
            () => cancelled++);

        AssertEx.Equal("Xem trước bản dịch", form.Text);
        AssertEx.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
        AssertEx.True(form.ShowInTaskbar, "Interactive preview should be discoverable in the taskbar.");
        AssertEx.True(
            form.AccessibleDescription?.Contains("focus", StringComparison.OrdinalIgnoreCase) == true,
            "Preview accessibility copy did not explain focus restoration.");
        AssertEx.Equal(
            "Xin chào",
            ((TextBox)form.Controls.Find("translationPreviewOriginal", true).Single()).Text);
        AssertEx.Equal(
            "Hello",
            ((TextBox)form.Controls.Find("translationPreviewTranslated", true).Single()).Text);

        InvokeClick((Button)form.Controls.Find("copyTranslationPreview", true).Single());
        AssertEx.Equal("Hello", copied);
        AssertEx.Equal(0, replaced);
        AssertEx.Equal(0, cancelled);
    }

    [KeyinaTest("translation preview replace and cancel actions fire once")]
    private static void PreviewActionsAreOneShot()
    {
        foreach (var actionName in new[]
                 {
                     "replaceTranslationPreview",
                     "cancelTranslationPreview",
                 })
        {
            var replaced = 0;
            var cancelled = 0;
            using var form = new TranslationPreviewForm(
                CreatePreview(),
                _ => replaced++,
                _ => { },
                () => cancelled++);
            var button = (Button)form.Controls.Find(actionName, true).Single();

            InvokeClick(button);
            InvokeClick(button);

            AssertEx.Equal(
                actionName == "replaceTranslationPreview" ? 1 : 0,
                replaced);
            AssertEx.Equal(
                actionName == "cancelTranslationPreview" ? 1 : 0,
                cancelled);
        }
    }

    private static TranslationPreview CreatePreview() => new(
        new SelectedTextCapture("Xin chào", (nint)42, (nint)420),
        "Xin chào",
        "Hello",
        "VI",
        "Fake",
        DateTimeOffset.UtcNow.AddMinutes(2));

    private static void InvokeClick(Button button)
    {
        var method = button.GetType().GetMethod(
            "OnClick",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Button click handler was not found.");
        _ = method.Invoke(button, [EventArgs.Empty]);
    }
}
