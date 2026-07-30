using System.Drawing;
using Keyina.Host.Core.Feedback;
using Keyina.Host.Windows.Feedback;

namespace Keyina.Host.Tests;

internal static class ForegroundPresentationTests
{
    [KeyinaTest("foreground coverage classifies exact and near fullscreen windows")]
    private static void ExactAndNearFullscreenWindowsAreDetected()
    {
        var monitor = new Rectangle(0, 0, 1920, 1080);

        AssertEx.Equal(
            ForegroundPresentationState.FullscreenLike,
            WindowsForegroundPresentationProbe.Classify(monitor, monitor));
        AssertEx.Equal(
            ForegroundPresentationState.FullscreenLike,
            WindowsForegroundPresentationProbe.Classify(
                new Rectangle(10, 5, 1900, 1070),
                monitor));
    }

    [KeyinaTest("foreground coverage keeps maximized and ordinary windows visual")]
    private static void MaximizedAndOrdinaryWindowsRemainWindowed()
    {
        var monitor = new Rectangle(0, 0, 1920, 1080);

        AssertEx.Equal(
            ForegroundPresentationState.Windowed,
            WindowsForegroundPresentationProbe.Classify(
                monitor,
                monitor,
                isMaximized: true));
        AssertEx.Equal(
            ForegroundPresentationState.Windowed,
            WindowsForegroundPresentationProbe.Classify(
                new Rectangle(0, 0, 1860, 1080),
                monitor));
        AssertEx.Equal(
            ForegroundPresentationState.Windowed,
            WindowsForegroundPresentationProbe.Classify(
                new Rectangle(200, 100, 1280, 720),
                monitor));
        AssertEx.Equal(
            ForegroundPresentationState.Windowed,
            WindowsForegroundPresentationProbe.Classify(
                new Rectangle(400, 0, 1920, 1080),
                monitor));
    }

    [KeyinaTest("foreground coverage handles negative monitor coordinates and invalid rectangles")]
    private static void NegativeCoordinatesAndInvalidRectanglesAreHandled()
    {
        var monitor = new Rectangle(-1920, 0, 1920, 1080);

        AssertEx.Equal(
            ForegroundPresentationState.FullscreenLike,
            WindowsForegroundPresentationProbe.Classify(
                new Rectangle(-1910, 5, 1900, 1070),
                monitor));
        AssertEx.Equal(
            ForegroundPresentationState.Unknown,
            WindowsForegroundPresentationProbe.Classify(Rectangle.Empty, monitor));
        AssertEx.Equal(
            ForegroundPresentationState.Unknown,
            WindowsForegroundPresentationProbe.Classify(
                new Rectangle(-1920, 0, 1920, 1080),
                Rectangle.Empty));
    }
}
