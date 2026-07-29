using System.Threading.Channels;

namespace Keyina.Host.Windows.Audio;

public sealed class AudioBufferOverflowException : Exception
{
    public AudioBufferOverflowException(string message)
        : base(message)
    {
    }
}

public sealed class BoundedAudioBuffer : IDisposable, IAsyncDisposable
{
    private readonly Channel<byte[]> channel = Channel.CreateUnbounded<byte[]>(
        new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
    private readonly object gate = new();
    private readonly int maxBufferedBytes;
    private int bufferedBytes;
    private bool completed;
    private bool disposed;
    private bool overflowed;

    public BoundedAudioBuffer(int maxBufferedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(maxBufferedBytes, 0);
        this.maxBufferedBytes = maxBufferedBytes;
    }

    public int BufferedBytes
    {
        get
        {
            lock (gate)
            {
                return bufferedBytes;
            }
        }
    }

    public bool IsOverflowed
    {
        get
        {
            lock (gate)
            {
                return overflowed;
            }
        }
    }

    public bool TryWrite(ReadOnlySpan<byte> audio)
    {
        if (audio.IsEmpty || (audio.Length & 1) != 0)
        {
            throw new ArgumentException(
                "Audio chunks must contain a non-empty even number of PCM16 bytes.",
                nameof(audio));
        }

        if (audio.Length > maxBufferedBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(audio),
                "One audio chunk cannot exceed the entire queue capacity.");
        }

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (completed)
            {
                return false;
            }

            if (bufferedBytes > maxBufferedBytes - audio.Length)
            {
                overflowed = true;
                completed = true;
                channel.Writer.TryComplete(new AudioBufferOverflowException(
                    "Keyina microphone audio exceeded the two-second queue budget."));
                return false;
            }

            var owned = audio.ToArray();
            if (!channel.Writer.TryWrite(owned))
            {
                return false;
            }

            bufferedBytes += owned.Length;
            return true;
        }
    }

    public async ValueTask<ReadOnlyMemory<byte>> ReadAsync(
        CancellationToken cancellationToken)
    {
        var audio = await channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        lock (gate)
        {
            bufferedBytes -= audio.Length;
        }

        return audio;
    }

    public void Complete(Exception? error = null)
    {
        lock (gate)
        {
            if (completed)
            {
                return;
            }

            completed = true;
            channel.Writer.TryComplete(error);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            completed = true;
            channel.Writer.TryComplete();
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
