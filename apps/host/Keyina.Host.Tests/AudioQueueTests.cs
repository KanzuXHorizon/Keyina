using System.Threading.Channels;
using Keyina.Host.Windows.Audio;

namespace Keyina.Host.Tests;

internal static class AudioQueueTests
{
    [KeyinaTest("audio queue accepts exactly two seconds and rejects overflow without dropping existing chunks")]
    private static void QueueIsBoundedByBytes() => Run(async () =>
    {
        await using var queue = new BoundedAudioBuffer(maxBufferedBytes: 64_000);
        AssertEx.True(queue.TryWrite(new byte[32_000]), "First second was rejected.");
        AssertEx.True(queue.TryWrite(new byte[32_000]), "Second second was rejected.");
        AssertEx.True(!queue.TryWrite(new byte[2]), "Queue accepted audio past two seconds.");
        AssertEx.True(queue.IsOverflowed, "Overflow state was not recorded.");

        var first = await queue.ReadAsync(CancellationToken.None);
        var second = await queue.ReadAsync(CancellationToken.None);
        AssertEx.Equal(32_000, first.Length);
        AssertEx.Equal(32_000, second.Length);
        AssertEx.Equal(0, queue.BufferedBytes);
    });

    [KeyinaTest("audio queue copies producer buffers and exposes completion")]
    private static void QueueOwnsItsBuffers() => Run(async () =>
    {
        await using var queue = new BoundedAudioBuffer(maxBufferedBytes: 128);
        var source = new byte[] { 1, 2, 3, 4 };
        AssertEx.True(queue.TryWrite(source), "Queue rejected a valid chunk.");
        source[0] = 99;

        var queued = await queue.ReadAsync(CancellationToken.None);
        AssertEx.True(queued.Span.SequenceEqual(new byte[] { 1, 2, 3, 4 }),
            "Queue retained the producer-owned buffer.");

        queue.Complete();
        await AssertThrowsAsync<ChannelClosedException>(() =>
            queue.ReadAsync(CancellationToken.None).AsTask());
    });

    [KeyinaTest("audio queue validates chunk alignment and size")]
    private static void QueueRejectsInvalidChunks()
    {
        AssertThrows<ArgumentOutOfRangeException>(() => _ = new BoundedAudioBuffer(0));
        using var queue = new BoundedAudioBuffer(maxBufferedBytes: 64);
        AssertThrows<ArgumentException>(() => queue.TryWrite(ReadOnlySpan<byte>.Empty));
        AssertThrows<ArgumentException>(() => queue.TryWrite(new byte[3]));
        AssertThrows<ArgumentOutOfRangeException>(() => queue.TryWrite(new byte[66]));
    }

    private static async Task AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void Run(Func<Task> action) => action().GetAwaiter().GetResult();
}
