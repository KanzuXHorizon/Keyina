using System.Text;
using Keyina.Host.Windows.Typing;

namespace Keyina.Host.Tests;

internal static class TypingDiagnosticTraceTests
{
    [KeyinaTest("focused typing diagnostics reject inactive and wrong target records")]
    private static void RejectsInactiveAndWrongTargetRecords()
    {
        TypingDiagnosticTrace.ClearAndDisable();
        var keyboardEvent = CreateKeyEvent('S', new Rune('s'), scanCode: 0x1F);

        TypingDiagnosticTrace.RecordPhysical(
            keyboardEvent,
            new VietnameseTypingContext(42, (nint)100, ShouldBypassTyping: false));
        AssertEx.Equal(0, TypingDiagnosticTrace.Snapshot().Count);

        TypingDiagnosticTrace.Activate((nint)100);
        TypingDiagnosticTrace.RecordPhysical(
            keyboardEvent,
            new VietnameseTypingContext(42, (nint)101, ShouldBypassTyping: false));
        TypingDiagnosticTrace.RecordOutput(
            (nint)101,
            "TextChanged",
            "s",
            selectionStart: 1,
            selectionLength: 0);

        AssertEx.Equal(0, TypingDiagnosticTrace.Snapshot().Count);
        TypingDiagnosticTrace.ClearAndDisable();
    }

    [KeyinaTest("focused typing diagnostics capture exact target key engine and output evidence")]
    private static void CapturesTargetEvidence()
    {
        TypingDiagnosticTrace.ClearAndDisable();
        TypingDiagnosticTrace.Activate((nint)200);
        var context = new VietnameseTypingContext(
            ForegroundProcessId: 42,
            FocusWindow: (nint)200,
            ShouldBypassTyping: false);
        var keyboardEvent = CreateKeyEvent('S', new Rune('s'), scanCode: 0x1F);

        TypingDiagnosticTrace.RecordPhysical(keyboardEvent, context);
        TypingDiagnosticTrace.RecordEngine(
            "transform",
            keyboardEvent,
            context,
            "backspaces=2;insert=\"á\"");
        TypingDiagnosticTrace.RecordOutput(
            (nint)200,
            "TextChanged",
            "cá",
            selectionStart: 2,
            selectionLength: 0);

        var entries = TypingDiagnosticTrace.Snapshot();
        AssertEx.Equal(3, entries.Count);
        AssertEx.Equal(TypingDiagnosticTraceKind.Physical, entries[0].Kind);
        AssertEx.Equal((int)'S', entries[0].VirtualKey);
        AssertEx.Equal((uint)0x1F, entries[0].ScanCode);
        AssertEx.Equal("s", entries[0].Character);
        AssertEx.Equal(TypingDiagnosticTraceKind.Engine, entries[1].Kind);
        AssertEx.True(
            entries[1].Detail.Contains('á'),
            "Engine trace did not preserve the sandbox insertion result.");
        AssertEx.Equal(TypingDiagnosticTraceKind.Output, entries[2].Kind);
        AssertEx.True(
            entries[2].Detail.Contains("cá", StringComparison.Ordinal),
            "Output trace did not preserve the visible sandbox result.");

        var formatted = TypingDiagnosticTrace.FormatSnapshot();
        AssertEx.True(
            formatted.Contains("VK=S", StringComparison.Ordinal) &&
            formatted.Contains("TextChanged", StringComparison.Ordinal),
            "Formatted trace omitted key or output evidence.");
        TypingDiagnosticTrace.ClearAndDisable();
    }

    [KeyinaTest("focused typing diagnostics flag duplicate key down and clear sensitive content")]
    private static void FlagsDuplicateKeyDownAndClearsSensitiveContent()
    {
        TypingDiagnosticTrace.ClearAndDisable();
        TypingDiagnosticTrace.Activate((nint)300);
        var context = new VietnameseTypingContext(42, (nint)300, ShouldBypassTyping: false);
        var keyboardEvent = CreateKeyEvent('S', new Rune('s'), scanCode: 0x1F);

        TypingDiagnosticTrace.RecordPhysical(keyboardEvent, context);
        TypingDiagnosticTrace.RecordPhysical(keyboardEvent, context);

        var anomalies = TypingDiagnosticTrace.Snapshot(TypingDiagnosticTraceKind.Anomaly);
        AssertEx.Equal(1, anomalies.Count);
        AssertEx.True(
            anomalies[0].Detail.Contains("duplicate", StringComparison.OrdinalIgnoreCase),
            "Repeated key down was not classified for double-key debugging.");

        TypingDiagnosticTrace.RecordOutput(
            (nint)300,
            "TextChanged",
            "nội dung nhạy cảm",
            selectionStart: 18,
            selectionLength: 0);
        TypingDiagnosticTrace.ClearAndDisable();

        AssertEx.False(TypingDiagnosticTrace.IsEnabled,
            "Closing the diagnostic session must disable raw capture.");
        AssertEx.Equal(0, TypingDiagnosticTrace.Snapshot().Count);
    }

    private static VietnameseKeyboardEvent CreateKeyEvent(
        int virtualKey,
        Rune character,
        uint scanCode) => new(
            VirtualKey: virtualKey,
            IsKeyDown: true,
            IsInjected: false,
            ExtraInfo: 0,
            Shift: false,
            Control: false,
            Alt: false,
            Windows: false,
            Character: character,
            ScanCode: scanCode);
}
