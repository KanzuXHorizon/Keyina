using System.Reflection;
using Keyina.Host.Translation;
using Keyina.Host.UI;
using Keyina.Host.UI.Fluent;

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
        AssertEx.Equal(FormBorderStyle.None, form.FormBorderStyle);
        AssertEx.True(
            form.UsesResizableWindowStyle,
            "Borderless translation reader did not expose the native resize frame.");
        AssertEx.True(
            form.SupportsHeaderDrag,
            "Translation reader header cannot move the borderless window.");
        AssertEx.False(form.MaximizeBox, "Borderless translation overlay must not expose maximize chrome.");
        AssertEx.False(form.MinimizeBox, "Borderless translation overlay must not expose minimize chrome.");
        AssertEx.Equal(1, form.Controls.Find("closeTranslationPreview", true).Length);
        AssertEx.True(form.MinimumSize.Width >= 460,
            "Translation reader minimum width is too narrow for readable text.");
        AssertEx.Equal(1, form.Controls.Find("translationPreviewSourceCard", true).Length);
        AssertEx.Equal("Xin chào", ((Label)form.Controls.Find("translationPreviewSource", true).Single()).Text);
        var reader = (RichTextBox)form.Controls
            .Find("translationPreviewTranslated", true).Single();
        AssertEx.Equal("Hello", reader.Text);
        AssertEx.True(reader.ReadOnly, "Translation reader must be read-only.");
        AssertEx.True(reader.WordWrap, "Translation reader must wrap long lines.");
        AssertEx.Equal(RichTextBoxScrollBars.Vertical, reader.ScrollBars);
        AssertEx.Equal(0, reader.SelectionLength);

        var replaceButton = (FluentButton)form.Controls
            .Find("replaceTranslationPreview", true).Single();
        var copyButton = (FluentButton)form.Controls
            .Find("copyTranslationPreview", true).Single();
        AssertEx.Equal(FluentButtonKind.Primary, replaceButton.Kind);
        AssertEx.Equal(FluentButtonKind.Secondary, copyButton.Kind);
        AssertEx.True(
            ReferenceEquals(form.AcceptButton, replaceButton),
            "Enter should activate the replace action in a replace-preview workflow.");
        AssertEx.Equal(1, form.Controls.Find("translationPreviewShortcutHint", true).Length);

        InvokeClick(copyButton);
        AssertEx.Equal("Hello", copied);
        AssertEx.Equal(0, replaced);
        AssertEx.Equal(0, cancelled);

        InvokeClick(replaceButton);
        AssertEx.Equal(1, replaced);
    }

    [KeyinaTest("translation reader minimum size keeps shortcuts and actions visible")]
    private static void PreviewMinimumSizeDoesNotClipFooter()
    {
        using var form = new TranslationPreviewForm(
            CreatePreview(),
            _ => { },
            _ => { },
            () => { })
        {
            Size = new Size(600, 340),
        };
        form.Opacity = 0;
        form.Show();
        Application.DoEvents();
        form.PerformLayout();

        var hint = form.Controls.Find("translationPreviewShortcutHint", true).Single();
        var replace = form.Controls.Find("replaceTranslationPreview", true).Single();
        var copy = form.Controls.Find("copyTranslationPreview", true).Single();
        var cancel = form.Controls.Find("cancelTranslationPreview", true).Single();
        var reader = form.Controls.Find("translationPreviewReaderCard", true).Single();

        AssertEx.True(hint.Width >= 120, "Shortcut hint collapsed at the minimum size.");
        AssertEx.True(reader.Height >= 48, "Reader content area became too short at minimum size.");
        AssertEx.Equal(1, form.Controls.Find("translationPreviewSourceCard", true).Length);
        foreach (var control in new[] { hint, replace, copy, cancel })
        {
            var bounds = form.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
            AssertEx.True(bounds.Left >= 0 && bounds.Top >= 0,
                $"{control.Name} began outside the client area.");
            AssertEx.True(bounds.Right <= form.ClientSize.Width && bounds.Bottom <= form.ClientSize.Height,
                $"{control.Name} was clipped at the minimum size.");
        }
    }

    [KeyinaTest("expired translation preview defaults Enter to copy")]
    private static void ExpiredPreviewUsesCopyAsSafeDefault()
    {
        var copied = string.Empty;
        var preview = CreatePreview() with
        {
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(-1),
        };
        using var form = new TranslationPreviewForm(
            preview,
            _ => throw new InvalidOperationException("Expired preview must not replace text."),
            text => copied = text,
            () => { });

        var replace = (Button)form.Controls.Find("replaceTranslationPreview", true).Single();
        var copy = (Button)form.Controls.Find("copyTranslationPreview", true).Single();
        var hint = (Label)form.Controls.Find("translationPreviewShortcutHint", true).Single();

        AssertEx.False(replace.Enabled, "Expired preview still allowed replacement.");
        AssertEx.True(ReferenceEquals(form.AcceptButton, copy),
            "Enter did not switch to the safe copy action after preview expiry.");
        AssertEx.True(hint.Text.Contains("Sao chép", StringComparison.Ordinal),
            "Expired preview shortcut hint did not explain the new default action.");
        InvokeClick(copy);
        AssertEx.Equal("Hello", copied);
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
