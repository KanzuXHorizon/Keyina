using System.Text;
using System.Threading.Channels;
using Keyina.Host.Core.Ipc;
using Keyina.Host.Core.Speech;
using Keyina.Speechmatics;

namespace Keyina.Host.Speech;

public sealed record SpeechSelfTestResult(
    bool Success,
    string Code,
    int FinalTranscriptCount,
    bool TransportClosed);

public static class SpeechSelfTest
{
    public static async Task<SpeechSelfTestResult> RunAsync(
        CancellationToken cancellationToken)
    {
        var transport = new SelfTestTransport();
        try
        {
            await using var session = new SpeechmaticsRealtimeSession(
                SpeechmaticsOptions.VietnameseDefault,
                transport,
                "self-test-token");

            await session.StartAsync(cancellationToken).ConfigureAwait(false);
            await session.SendAudioAsync(
                new byte[] { 0, 0 },
                cancellationToken).ConfigureAwait(false);
            await session.StopAsync(
                TimeSpan.FromSeconds(2),
                cancellationToken).ConfigureAwait(false);

            var speechEvent = await session.ReadEventAsync(cancellationToken)
                .ConfigureAwait(false);
            if (speechEvent.Kind != SpeechEventKind.FinalTranscript)
            {
                return new SpeechSelfTestResult(
                    false,
                    "speech_self_test_missing_final",
                    0,
                    transport.Closed);
            }

            var aggregator = new TranscriptAggregator();
            var update = aggregator.Apply(
                new TranscriptEvent(
                    TranscriptEventKind.Final,
                    speechEvent.Text,
                    speechEvent.StartTimeSeconds,
                    speechEvent.EndTimeSeconds),
                new IpcSessionId(1, 2),
                focusGeneration: 3);
            if (update.FinalEnvelope is null)
            {
                return new SpeechSelfTestResult(
                    false,
                    "speech_self_test_missing_ipc",
                    0,
                    transport.Closed);
            }

            var frame = IpcFrameCodec.Encode(update.FinalEnvelope);
            var decodeStatus = IpcFrameCodec.TryDecode(
                frame,
                out var decoded,
                out var consumed,
                out _);
            var success = decodeStatus == IpcDecodeStatus.Success &&
                consumed == frame.Length &&
                decoded == update.FinalEnvelope;
            return new SpeechSelfTestResult(
                success,
                success ? "speech_self_test_ok" : "speech_self_test_ipc_failed",
                success ? 1 : 0,
                transport.Closed);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new SpeechSelfTestResult(
                false,
                "speech_self_test_failed",
                0,
                transport.Closed);
        }
    }

    private sealed class SelfTestTransport : ISpeechmaticsTransport
    {
        private readonly Channel<SpeechTransportMessage> incoming =
            Channel.CreateUnbounded<SpeechTransportMessage>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = false,
                });
        private int audioSequence;
        private bool disposed;

        public bool Closed { get; private set; }

        public Task ConnectAsync(
            Uri endpoint,
            string authorizationHeader,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (endpoint != SpeechmaticsOptions.VietnameseDefault.Endpoint ||
                !authorizationHeader.StartsWith("Bearer ", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Self-test transport received invalid connection settings.");
            }

            return Task.CompletedTask;
        }

        public ValueTask SendTextAsync(
            ReadOnlyMemory<byte> utf8Json,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(disposed, this);
            var json = Encoding.UTF8.GetString(utf8Json.Span);
            if (json.Contains("\"StartRecognition\"", StringComparison.Ordinal))
            {
                incoming.Writer.TryWrite(SpeechTransportMessage.Text(
                    "{\"message\":\"RecognitionStarted\",\"id\":\"self-test\"}"u8.ToArray()));
            }
            else if (json.Contains("\"EndOfStream\"", StringComparison.Ordinal))
            {
                incoming.Writer.TryWrite(SpeechTransportMessage.Text(
                    "{\"message\":\"AddTranscript\",\"metadata\":{\"transcript\":\"xin chào\",\"start_time\":0.0,\"end_time\":0.1}}"u8.ToArray()));
                incoming.Writer.TryWrite(SpeechTransportMessage.Text(
                    "{\"message\":\"EndOfTranscript\"}"u8.ToArray()));
            }
            else
            {
                throw new InvalidOperationException("Self-test transport received an unknown text frame.");
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask SendBinaryAsync(
            ReadOnlyMemory<byte> audio,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(disposed, this);
            if (audio.IsEmpty || (audio.Length & 1) != 0)
            {
                throw new InvalidOperationException("Self-test received invalid PCM audio.");
            }

            var sequence = Interlocked.Increment(ref audioSequence);
            incoming.Writer.TryWrite(SpeechTransportMessage.Text(
                Encoding.UTF8.GetBytes($"{{\"message\":\"AudioAdded\",\"seq_no\":{sequence}}}")));
            return ValueTask.CompletedTask;
        }

        public IAsyncEnumerable<SpeechTransportMessage> ReceiveAsync(
            CancellationToken cancellationToken) =>
            incoming.Reader.ReadAllAsync(cancellationToken);

        public Task CloseAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Closed = true;
            incoming.Writer.TryComplete();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (disposed)
            {
                return ValueTask.CompletedTask;
            }

            disposed = true;
            incoming.Writer.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
