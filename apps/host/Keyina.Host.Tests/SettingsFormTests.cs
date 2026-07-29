using Keyina.Host.UI;

namespace Keyina.Host.Tests;

internal static class SettingsFormTests
{
    [KeyinaTest("settings form is accessible DPI-aware and exposes all production sections")]
    private static void SettingsFormStructureIsComplete()
    {
        using var form = new SettingsForm(
            SettingsSnapshot.Sample,
            SettingsActions.NoOp);

        AssertEx.Equal("Keyina", form.Text);
        AssertEx.Equal("Keyina settings", form.AccessibleName);
        AssertEx.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
        AssertEx.True(form.MinimumSize.Width >= 760, "Settings minimum width is too small.");
        AssertEx.True(form.MinimumSize.Height >= 560, "Settings minimum height is too small.");
        AssertEx.True(form.ShowInTaskbar, "Settings window should appear in the taskbar when opened.");
        AssertEx.Equal(FormStartPosition.CenterScreen, form.StartPosition);

        foreach (var name in new[]
                 {
                     "navOverview",
                     "navTyping",
                     "navSpeech",
                     "navHotkeys",
                     "navSnippets",
                     "navDiagnostics",
                 })
        {
            AssertEx.Equal(1, form.Controls.Find(name, searchAllChildren: true).Length);
        }
    }

    [KeyinaTest("settings form protects speech credentials and exposes familiar controls")]
    private static void SensitiveAndFamiliarControlsAreCorrect()
    {
        using var form = new SettingsForm(
            SettingsSnapshot.Sample,
            SettingsActions.NoOp);

        var apiKey = (TextBox)form.Controls.Find("speechApiKey", true).Single();
        AssertEx.True(apiKey.UseSystemPasswordChar, "Speech API key was not masked.");
        AssertEx.Equal(string.Empty, apiKey.Text);

        var vietnamese = (CheckBox)form.Controls.Find("vietnameseToggle", true).Single();
        var startup = (CheckBox)form.Controls.Find("startupToggle", true).Single();
        AssertEx.True(vietnamese.Checked, "Vietnamese input should be enabled in the sample state.");
        AssertEx.True(startup.Checked, "Startup should be enabled in the sample state.");

        var saveButton = (Button)form.Controls.Find("saveSpeechKey", true).Single();
        AssertEx.True(!saveButton.Enabled, "Empty API key should not be saveable.");
    }

    [KeyinaTest("settings form applies runtime snapshots without exposing secret text")]
    private static void SnapshotUpdatesVisibleState()
    {
        using var form = new SettingsForm(
            SettingsSnapshot.Sample,
            SettingsActions.NoOp);
        form.ApplySnapshot(SettingsSnapshot.Sample with
        {
            VietnameseEnabled = false,
            StartupEnabled = false,
            Listening = true,
            SpeechCredentialConfigured = false,
            StatusMessage = "Listening",
        });

        AssertEx.True(
            !((CheckBox)form.Controls.Find("vietnameseToggle", true).Single()).Checked,
            "Vietnamese toggle did not update.");
        AssertEx.True(
            !((CheckBox)form.Controls.Find("startupToggle", true).Single()).Checked,
            "Startup toggle did not update.");
        AssertEx.Equal(
            "Listening",
            ((Label)form.Controls.Find("statusMessage", true).Single()).Text);
        AssertEx.Equal(
            "Not configured",
            ((Label)form.Controls.Find("speechCredentialStatus", true).Single()).Text);
    }
}
