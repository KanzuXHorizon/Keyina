using System.Reflection;
using Keyina.Host.Core.Hotkeys;
using Keyina.Host.UI;

namespace Keyina.Host.Tests;

internal static class HotkeyCaptureDialogTests
{
    [KeyinaTest("hotkey capture dialog records a valid modified primary key")]
    private static void CapturesModifiedPrimaryKey()
    {
        using var dialog = new HotkeyCaptureDialog(
            HotkeyCommand.TranslateSelection,
            HotkeyPreferences.Default);

        SendKey(dialog, Keys.Control | Keys.Shift | Keys.K);

        AssertEx.Equal(
            new HotkeyChord(
                HotkeyModifiers.Control | HotkeyModifiers.Shift,
                VirtualKey.K),
            dialog.CapturedChord);
        var save = (Button)dialog.Controls.Find("saveHotkeyCapture", true).Single();
        AssertEx.True(save.Enabled, "Valid captured chord did not enable save.");
        var keycap = (Label)dialog.Controls.Find("hotkeyCaptureKeycap", true).Single();
        AssertEx.Equal("Ctrl + Shift + K", keycap.Text);
    }

    [KeyinaTest("hotkey capture dialog records a modifier-only input toggle gesture")]
    private static void CapturesModifierOnlyGesture()
    {
        using var dialog = new HotkeyCaptureDialog(
            HotkeyCommand.ToggleVietnamese,
            HotkeyPreferences.Default);

        SendKey(dialog, Keys.Alt | Keys.Shift | Keys.ShiftKey);

        AssertEx.Equal(
            new HotkeyChord(
                HotkeyModifiers.Alt | HotkeyModifiers.Shift,
                VirtualKey.None),
            dialog.CapturedChord);
    }

    [KeyinaTest("hotkey capture dialog rejects a shortcut already used by Keyina")]
    private static void RejectsDuplicateShortcut()
    {
        using var dialog = new HotkeyCaptureDialog(
            HotkeyCommand.TranslateSelection,
            HotkeyPreferences.Default);

        SendKey(dialog, Keys.Control | Keys.Alt | Keys.V);

        AssertEx.Equal<HotkeyChord?>(null, dialog.CapturedChord);
        var status = (Label)dialog.Controls.Find("hotkeyCaptureStatus", true).Single();
        AssertEx.True(
            status.Text.Contains("đã được dùng", StringComparison.OrdinalIgnoreCase),
            "Duplicate shortcut did not explain the conflict.");
        var save = (Button)dialog.Controls.Find("saveHotkeyCapture", true).Single();
        AssertEx.False(save.Enabled, "Duplicate shortcut remained saveable.");
    }

    [KeyinaTest("hotkey capture dialog uses Escape to cancel capture")]
    private static void EscapeCancelsCapture()
    {
        using var dialog = new HotkeyCaptureDialog(
            HotkeyCommand.ToggleDictation,
            HotkeyPreferences.Default);

        SendKey(dialog, Keys.Escape);

        AssertEx.Equal(DialogResult.Cancel, dialog.DialogResult);
        AssertEx.Equal<HotkeyChord?>(null, dialog.CapturedChord);
    }

    private static void SendKey(HotkeyCaptureDialog dialog, Keys keyData)
    {
        var method = typeof(HotkeyCaptureDialog).GetMethod(
            "OnKeyDown",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Hotkey capture key handler was not found.");
        _ = method.Invoke(dialog, [new KeyEventArgs(keyData)]);
    }
}
