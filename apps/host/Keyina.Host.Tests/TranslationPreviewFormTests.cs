using System.Reflection;
using Keyina.Host.Translation;
using Keyina.Host.UI;

namespace Keyina.Host.Tests;

internal static class TranslationPreviewFormTests
{
    [KeyinaTest("translation overlay exposes translated text without replacement controls")]
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

        AssertEx.Equal("Bản dịch", form.Text);
        AssertEx.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
        AssertEx.False(form.ShowInTaskbar, "Translation overlay should not create a taskbar item.");
        AssertEx.True(form.TopMost, "Translation overlay should remain visible above the selected app.");
        AssertEx.True(
            form.AccessibleDescription?.Contains("không thay đổi", StringComparison.OrdinalIgnoreCase) == true,
            "Overlay accessibility copy did not explain that original text remains unchanged.");
        AssertEx.Equal(0, form.Controls.Find("translationPreviewOriginal", true).Length);
        AssertEx.Equal(0, form.Controls.Find("replaceTranslationPreview", true).Length);
        AssertEx.Equal(
            "Hello",
            ((TextBox)form.Controls.Find("translationPreviewTranslated", true).Single()).Text);

        InvokeClick((Button)form.Controls.Find("copyTranslationPreview", true).Single());
        AssertEx.Equal("Hello", copied);
        AssertEx.Equal(0, replaced);
        AssertEx.Equal(0, cancelled);
    }

    [KeyinaTest("translation overlay cancel action fires once without replacing")]
    private static void PreviewActionsAreOneShot()
    {
        var replaced = 0;
        var cancelled = 0;
        using var form = new TranslationPreviewForm(
            CreatePreview(),
            _ => replaced++,
            _ => { },
            () => cancelled++);
        var button = (Button)form.Controls.Find("cancelTranslationPreview", true).Single();

        InvokeClick(button);
        InvokeClick(button);

        AssertEx.Equal(0, replaced);
        AssertEx.Equal(1, cancelled);
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
