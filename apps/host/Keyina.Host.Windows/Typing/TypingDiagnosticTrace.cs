using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Keyina.Host.Windows.Typing;

public enum TypingDiagnosticTraceKind
{
    Physical,
    Engine,
    Output,
    Anomaly,
}

public sealed record TypingDiagnosticTraceEntry(
    long Sequence,
    long TimestampTicks,
    double ElapsedMilliseconds,
    TypingDiagnosticTraceKind Kind,
    string Event,
    int VirtualKey,
    uint ScanCode,
    string Character,
    bool IsKeyDown,
    bool IsInjected,
    bool Shift,
    bool Control,
    bool Alt,
    bool Windows,
    string Detail);

public static class TypingDiagnosticTrace
{
    private const int Capacity = 1024;
    private const int MaximumDetailLength = 4096;
    private static readonly object Gate = new();
    private static readonly Queue<TypingDiagnosticTraceEntry> Entries = new(Capacity);
    private static readonly HashSet<int> PressedKeys = [];
    private static long sequence;
    private static long startedAt;
    private static nint targetWindow;
    private static int enabled;

    public static bool IsEnabled => Volatile.Read(ref enabled) != 0;

    public static void Activate(nint focusWindow)
    {
        ArgumentOutOfRangeException.ThrowIfZero(focusWindow);
        lock (Gate)
        {
            if (startedAt == 0)
            {
                startedAt = Stopwatch.GetTimestamp();
            }
            PressedKeys.Clear();
            Interlocked.Exchange(ref targetWindow, focusWindow);
            Volatile.Write(ref enabled, 1);
        }
    }

    public static void Deactivate(nint focusWindow)
    {
        if (focusWindow == 0)
        {
            return;
        }
        lock (Gate)
        {
            if (ReadTargetWindow() != focusWindow)
            {
                return;
            }
            PressedKeys.Clear();
            Volatile.Write(ref enabled, 0);
        }
    }

    public static void Clear()
    {
        lock (Gate)
        {
            Entries.Clear();
            PressedKeys.Clear();
            sequence = 0;
            startedAt = Stopwatch.GetTimestamp();
        }
    }

    public static void ClearAndDisable()
    {
        lock (Gate)
        {
            Volatile.Write(ref enabled, 0);
            Interlocked.Exchange(ref targetWindow, 0);
            Entries.Clear();
            PressedKeys.Clear();
            sequence = 0;
            startedAt = 0;
        }
    }

    public static void RecordPhysical(
        VietnameseKeyboardEvent keyboardEvent,
        VietnameseTypingContext context)
    {
        if (!MatchesTarget(context.FocusWindow))
        {
            return;
        }

        lock (Gate)
        {
            if (!MatchesTarget(context.FocusWindow))
            {
                return;
            }

            var duplicateKeyDown = keyboardEvent.IsKeyDown &&
                !PressedKeys.Add(keyboardEvent.VirtualKey);
            if (!keyboardEvent.IsKeyDown)
            {
                PressedKeys.Remove(keyboardEvent.VirtualKey);
            }

            AddEntry(
                TypingDiagnosticTraceKind.Physical,
                keyboardEvent.IsKeyDown ? "KeyDown" : "KeyUp",
                keyboardEvent,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"pid={context.ForegroundProcessId};repeat={duplicateKeyDown}"));

            if (duplicateKeyDown)
            {
                AddEntry(
                    TypingDiagnosticTraceKind.Anomaly,
                    "duplicate-keydown",
                    keyboardEvent,
                    "The same virtual key produced another KeyDown before KeyUp; possible auto-repeat or duplicate delivery.");
            }
        }
    }

    public static void RecordEngine(
        string eventName,
        VietnameseKeyboardEvent keyboardEvent,
        VietnameseTypingContext context,
        string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(detail);
        if (!MatchesTarget(context.FocusWindow))
        {
            return;
        }

        lock (Gate)
        {
            if (!MatchesTarget(context.FocusWindow))
            {
                return;
            }
            AddEntry(
                TypingDiagnosticTraceKind.Engine,
                eventName,
                keyboardEvent,
                detail);
        }
    }

    public static void RecordOutput(
        nint focusWindow,
        string eventName,
        string text,
        int selectionStart,
        int selectionLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(selectionStart);
        ArgumentOutOfRangeException.ThrowIfNegative(selectionLength);
        if (!MatchesTarget(focusWindow))
        {
            return;
        }

        lock (Gate)
        {
            if (!MatchesTarget(focusWindow))
            {
                return;
            }
            AddEntry(
                TypingDiagnosticTraceKind.Output,
                eventName,
                default,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"text=\"{Escape(text)}\";selectionStart={selectionStart};selectionLength={selectionLength}"));
        }
    }

    public static IReadOnlyList<TypingDiagnosticTraceEntry> Snapshot(
        TypingDiagnosticTraceKind? kind = null,
        int maximum = Capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);
        lock (Gate)
        {
            IEnumerable<TypingDiagnosticTraceEntry> selected = Entries;
            if (kind is not null)
            {
                selected = selected.Where(entry => entry.Kind == kind.Value);
            }
            return selected.TakeLast(Math.Min(maximum, Capacity)).ToArray();
        }
    }

    public static string FormatSnapshot(
        TypingDiagnosticTraceKind? kind = null,
        int maximum = Capacity) => string.Join(
            Environment.NewLine,
            Snapshot(kind, maximum).Select(FormatEntry));

    private static bool MatchesTarget(nint focusWindow) =>
        IsEnabled &&
        focusWindow != 0 &&
        ReadTargetWindow() == focusWindow;

    private static nint ReadTargetWindow() =>
        Interlocked.CompareExchange(ref targetWindow, 0, 0);

    private static void AddEntry(
        TypingDiagnosticTraceKind kind,
        string eventName,
        VietnameseKeyboardEvent keyboardEvent,
        string detail)
    {
        var timestamp = Stopwatch.GetTimestamp();
        var sessionStartedAt = startedAt == 0 ? timestamp : startedAt;
        var entry = new TypingDiagnosticTraceEntry(
            ++sequence,
            timestamp,
            Stopwatch.GetElapsedTime(sessionStartedAt, timestamp).TotalMilliseconds,
            kind,
            eventName,
            keyboardEvent.VirtualKey,
            keyboardEvent.ScanCode,
            keyboardEvent.Character.Value == 0
                ? string.Empty
                : keyboardEvent.Character.ToString(),
            keyboardEvent.IsKeyDown,
            keyboardEvent.IsInjected,
            keyboardEvent.Shift,
            keyboardEvent.Control,
            keyboardEvent.Alt,
            keyboardEvent.Windows,
            Bound(detail));
        if (Entries.Count == Capacity)
        {
            Entries.Dequeue();
        }
        Entries.Enqueue(entry);
    }

    private static string FormatEntry(TypingDiagnosticTraceEntry entry)
    {
        var builder = new StringBuilder(192);
        builder.Append('#')
            .Append(entry.Sequence.ToString("D4", CultureInfo.InvariantCulture))
            .Append(" +")
            .Append(entry.ElapsedMilliseconds.ToString("F2", CultureInfo.InvariantCulture))
            .Append(" ms [")
            .Append(entry.Kind)
            .Append("] ")
            .Append(entry.Event);
        if (entry.VirtualKey != 0)
        {
            builder.Append(" VK=")
                .Append(FormatVirtualKey(entry.VirtualKey))
                .Append(" Scan=0x")
                .Append(entry.ScanCode.ToString("X2", CultureInfo.InvariantCulture))
                .Append(" Char=\"")
                .Append(Escape(entry.Character))
                .Append("\" Mods=")
                .Append(entry.Shift ? 'S' : '-')
                .Append(entry.Control ? 'C' : '-')
                .Append(entry.Alt ? 'A' : '-')
                .Append(entry.Windows ? 'W' : '-')
                .Append(" Injected=")
                .Append(entry.IsInjected);
        }
        if (entry.Detail.Length > 0)
        {
            builder.Append(" | ").Append(entry.Detail);
        }
        return builder.ToString();
    }

    private static string FormatVirtualKey(int virtualKey) => virtualKey switch
    {
        >= 'A' and <= 'Z' => ((char)virtualKey).ToString(),
        >= '0' and <= '9' => ((char)virtualKey).ToString(),
        0x08 => "Backspace",
        0x09 => "Tab",
        0x0D => "Enter",
        0x1B => "Escape",
        0x20 => "Space",
        _ => string.Create(CultureInfo.InvariantCulture, $"0x{virtualKey:X2}"),
    };

    private static string Escape(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal);

    private static string Bound(string value) => value.Length <= MaximumDetailLength
        ? value
        : string.Concat(value.AsSpan(0, MaximumDetailLength - 1), "…");
}
