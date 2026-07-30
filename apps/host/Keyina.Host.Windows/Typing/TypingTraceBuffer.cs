using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Keyina.Host.Windows.Typing;

public static class TypingTraceBuffer
{
    private const int Capacity = 512;
    private static readonly TypingTraceEntry?[] Entries = new TypingTraceEntry?[Capacity];
    private static long sequence;
    private static int enabled;

    public static bool IsEnabled => Volatile.Read(ref enabled) != 0;

    public static void SetEnabled(bool value) =>
        Volatile.Write(ref enabled, value ? 1 : 0);

    public static void Clear()
    {
        SetEnabled(false);
        Array.Clear(Entries);
        Volatile.Write(ref sequence, 0);
    }

    public static void Record(
        string action,
        int virtualKey,
        int processId,
        bool shift = false,
        bool control = false,
        bool alt = false,
        bool windows = false,
        string? detail = null)
    {
        if (!IsEnabled)
        {
            return;
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        var next = Interlocked.Increment(ref sequence);
        var entry = new TypingTraceEntry(
            next,
            Stopwatch.GetTimestamp(),
            action,
            ClassifyVirtualKey(virtualKey),
            processId,
            shift,
            control,
            alt,
            windows,
            detail ?? string.Empty);
        Volatile.Write(ref Entries[(next - 1) % Capacity], entry);
    }

    public static IReadOnlyList<TypingTraceEntry> Snapshot(int maximum = Capacity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximum);
        var end = Volatile.Read(ref sequence);
        if (end == 0)
        {
            return Array.Empty<TypingTraceEntry>();
        }

        var count = Math.Min(Math.Min(maximum, Capacity), checked((int)Math.Min(end, int.MaxValue)));
        var start = end - count + 1;
        var snapshot = new List<TypingTraceEntry>(count);
        for (var expected = start; expected <= end; expected++)
        {
            var entry = Volatile.Read(ref Entries[(expected - 1) % Capacity]);
            if (entry is not null && entry.Sequence == expected)
            {
                snapshot.Add(entry);
            }
        }
        return snapshot;
    }

    public static string WriteSnapshot(string path, int maximum = Capacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("Trace path must be fully qualified.", nameof(path));
        }

        var directory = Path.GetDirectoryName(path)
            ?? throw new ArgumentException("Trace path needs a parent directory.", nameof(path));
        Directory.CreateDirectory(directory);
        var lines = Snapshot(maximum).Select(FormatEntry);
        File.WriteAllLines(path, lines, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static string FormatEntry(TypingTraceEntry entry) => string.Create(
        CultureInfo.InvariantCulture,
        $"seq={entry.Sequence} ticks={entry.TimestampTicks} action={entry.Action} " +
        $"key={entry.KeyClass} pid={entry.ProcessId} " +
        $"mods={(entry.Shift ? 'S' : '-')}{(entry.Control ? 'C' : '-')}{(entry.Alt ? 'A' : '-')}{(entry.Windows ? 'W' : '-')} " +
        $"detail={entry.Detail}");

    private static string ClassifyVirtualKey(int virtualKey) => virtualKey switch
    {
        >= 0x41 and <= 0x5A => "letter",
        >= 0x30 and <= 0x39 => "digit",
        0x08 => "backspace",
        0x20 => "space",
        0x09 or 0x0D or 0x1B => "control",
        _ => "other",
    };
}

public sealed record TypingTraceEntry(
    long Sequence,
    long TimestampTicks,
    string Action,
    string KeyClass,
    int ProcessId,
    bool Shift,
    bool Control,
    bool Alt,
    bool Windows,
    string Detail);
