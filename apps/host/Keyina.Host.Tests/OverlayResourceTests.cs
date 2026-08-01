using System.Diagnostics;
using System.Runtime.InteropServices;
using Keyina.Host.Core.Speech;
using Keyina.Host.Translation;
using Keyina.Host.UI;

namespace Keyina.Host.Tests;

internal static class OverlayResourceTests
{
    private const uint GdiObjects = 0;
    private const uint UserObjects = 1;

    [KeyinaTest("translation and dictation overlays release native UI resources")]
    private static void OverlaysReleaseNativeResources()
    {
        using var process = Process.GetCurrentProcess();
        WarmUp();
        ForceCleanup();
        var beforeGdi = GetGuiResources(process.Handle, GdiObjects);
        var beforeUser = GetGuiResources(process.Handle, UserObjects);

        for (var index = 0; index < 30; index++)
        {
            using (var translation = new TranslationPreviewForm(
                       CreatePreview(index),
                       _ => { },
                       _ => { },
                       () => { })
                   {
                       Opacity = 0,
                       Location = new Point(-10_000, -10_000),
                   })
            {
                translation.Show();
                translation.PerformLayout();
                translation.Close();
            }

            using (var dictation = new DictationOverlayForm
                   {
                       Opacity = 0,
                   })
            {
                dictation.Present(new DictationState(
                    DictationStatus.Listening,
                    PartialText: $"partial {index}",
                    CommittedText: $"committed transcript {index}",
                    FinalSegments: index,
                    ErrorCode: null));
                dictation.HideOverlay();
            }
        }

        ForceCleanup();
        var afterGdi = GetGuiResources(process.Handle, GdiObjects);
        var afterUser = GetGuiResources(process.Handle, UserObjects);
        AssertEx.True(
            afterGdi <= beforeGdi + 4,
            $"Overlay lifecycle leaked GDI objects: {beforeGdi} -> {afterGdi}.");
        AssertEx.True(
            afterUser <= beforeUser + 4,
            $"Overlay lifecycle leaked USER objects: {beforeUser} -> {afterUser}.");
    }

    private static void WarmUp()
    {
        for (var index = 0; index < 30; index++)
        {
            using (var translation = new TranslationPreviewForm(
                       CreatePreview(index),
                       _ => { },
                       _ => { },
                       () => { })
                   {
                       Opacity = 0,
                       Location = new Point(-10_000, -10_000),
                   })
            {
                translation.Show();
                translation.Close();
            }

            using var dictation = new DictationOverlayForm { Opacity = 0 };
            dictation.Present(new DictationState(
                DictationStatus.Listening,
                PartialText: string.Empty,
                CommittedText: $"warm up {index}",
                FinalSegments: index,
                ErrorCode: null));
            dictation.HideOverlay();
        }
    }

    private static TranslationPreview CreatePreview(int index) => new(
        new SelectedTextCapture("source", 1, 2),
        "source",
        string.Join(' ', Enumerable.Repeat($"translated text {index}", 20)),
        "EN",
        "ResourceTest",
        DateTimeOffset.UtcNow.AddMinutes(2));

    private static void ForceCleanup()
    {
        Application.DoEvents();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Application.DoEvents();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetGuiResources(IntPtr process, uint flags);
}
