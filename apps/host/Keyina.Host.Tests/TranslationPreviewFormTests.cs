using System.Reflection;
using Keyina.Host.Translation;
using Keyina.Host.UI;

namespace Keyina.Host.Tests;

internal static class TranslationPreviewFormTests
{
    [KeyinaTest("translation overlay exposes an adaptive reader and complete actions")]
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
        AssertEx.Equal(FormBorderStyle.Sizable, form.FormBorderStyle);
        AssertEx.True(form.MinimumSize.Width >= 460,
            "Translation reader minimum width is too narrow for readable text.");
        AssertEx.Equal(0, form.Controls.Find("translationPreviewOriginal", true).Length);
        var reader = (RichTextBox)form.Controls
            .Find("translationPreviewTranslated", true).Single();
        AssertEx.Equal("Hello", reader.Text);
        AssertEx.True(reader.ReadOnly, "Translation reader must be read-only.");
        AssertEx.True(reader.WordWrap, "Translation reader must wrap long lines.");
        AssertEx.Equal(RichTextBoxScrollBars.Vertical, reader.ScrollBars);
        AssertEx.Equal(0, reader.SelectionLength);

        InvokeClick((Button)form.Controls.Find("copyTranslationPreview", true).Single());
        AssertEx.Equal("Hello", copied);
        AssertEx.Equal(0, replaced);
        AssertEx.Equal(0, cancelled);

        InvokeClick((Button)form.Controls.Find("replaceTranslationPreview", true).Single());
        AssertEx.Equal(1, replaced);
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
