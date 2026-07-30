using Keyina.Host.Windows.Typing;

namespace Keyina.Host.Tests;

internal static class TypingTraceBufferTests
{
    [KeyinaTest("typing trace is inert until explicitly enabled")]
    private static void TraceIsDisabledByDefault()
    {
        TypingTraceBuffer.Clear();

        TypingTraceBuffer.Record("transform", 'A', 42, detail: "backspaces=1");

        AssertEx.Equal(0, TypingTraceBuffer.Snapshot().Count);
    }

    [KeyinaTest("typing trace stores key categories instead of raw keys")]
    private static void TraceStoresContentFreeKeyCategories()
    {
        TypingTraceBuffer.Clear();
        TypingTraceBuffer.SetEnabled(true);
        try
        {
            TypingTraceBuffer.Record(
                "transform",
                'A',
                42,
                detail: "backspaces=1;insertUnits=1");

            var entry = TypingTraceBuffer.Snapshot().Single();
            AssertEx.Equal("letter", entry.KeyClass);
            AssertEx.Equal("transform", entry.Action);
            AssertEx.Equal("backspaces=1;insertUnits=1", entry.Detail);
        }
        finally
        {
            TypingTraceBuffer.Clear();
        }
    }
}
