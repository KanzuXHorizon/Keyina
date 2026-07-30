using System.IO.Pipes;
using Keyina.Host.Core.Ipc;
using Keyina.Host.Windows.Ipc;

namespace Keyina.Host.Tests;

internal static class NamedPipeEnvelopeServerTests
{
    [KeyinaTest("current session pipe name is deterministic and contains no user path")]
    private static void PipeNameIsStable()
    {
        var first = PipeEndpointName.ForCurrentSession();
        var second = PipeEndpointName.ForCurrentSession();
        AssertEx.Equal(first, second);
        AssertEx.True(
            first.StartsWith("Keyina.Host.v1.s", StringComparison.Ordinal),
            "Pipe name did not use the expected versioned session prefix.");
        AssertEx.True(!first.Contains('\\') && !first.Contains('/') && !first.Contains(':'),
            "Pipe name contained a user path or separator.");
    }

    [KeyinaTest("named pipe frame stream handles fragmented reads and exact UTF-8 frames")]
    private static void FrameStreamHandlesFragmentation() => Run(async () =>
    {
        var envelope = new IpcEnvelope(
            IpcMessageType.FinalTranscript,
            Flags: 0,
            new IpcSessionId(1, 2),
            FocusGeneration: 3,
            Payload: "xin chào");
        var frame = IpcFrameCodec.Encode(envelope);
        await using var stream = new FragmentedReadStream(frame, maximumChunk: 3);

        var decoded = await NamedPipeFrameProtocol.ReadAsync(stream, CancellationToken.None);
        AssertEx.Equal(envelope, decoded);
    });

    [KeyinaTest("current-user named pipe routes final text only to matching active focus")]
    private static void ServerRoutesMatchingFocus() => Run(async () =>
    {
        var pipeName = $"Keyina.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeEnvelopeServer(pipeName);
        await server.StartAsync(CancellationToken.None);
        await using var client = CreateClient(pipeName);
        await client.ConnectAsync(2_000, CancellationToken.None);

        var sessionId = new IpcSessionId(11, 22);
        var hello = new IpcEnvelope(
            IpcMessageType.Hello,
            Flags: 0,
            sessionId,
            FocusGeneration: 7,
            Payload: "pid=123;tid=456;cap=external_text");
        await NamedPipeFrameProtocol.WriteAsync(client, hello, CancellationToken.None);
        await WaitUntilAsync(() => server.ActiveTarget is not null);
        AssertEx.Equal(sessionId, server.ActiveTarget!.SessionId);
        AssertEx.Equal<ulong>(7, server.ActiveTarget.FocusGeneration);

        var final = new IpcEnvelope(
            IpcMessageType.FinalTranscript,
            Flags: 0,
            sessionId,
            FocusGeneration: 7,
            Payload: "ổn định");
        var route = await server.WriteToActiveAsync(final, CancellationToken.None);
        AssertEx.Equal(EnvelopeRouteResult.Written, route);
        var received = await NamedPipeFrameProtocol.ReadAsync(client, CancellationToken.None);
        AssertEx.Equal(final, received);

        var stale = final with { FocusGeneration = 6 };
        AssertEx.Equal(
            EnvelopeRouteResult.StaleTarget,
            await server.WriteToActiveAsync(stale, CancellationToken.None));
    });

    [KeyinaTest("active target wait survives the reconnect gap caused by a hotkey")]
    private static void ActiveTargetWaitSurvivesReconnectGap() => Run(async () =>
    {
        var pipeName = $"Keyina.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeEnvelopeServer(pipeName);
        await server.StartAsync(CancellationToken.None);

        await using (var previous = CreateClient(pipeName))
        {
            await previous.ConnectAsync(2_000, CancellationToken.None);
            await NamedPipeFrameProtocol.WriteAsync(
                previous,
                Hello(new IpcSessionId(1, 2), 7),
                CancellationToken.None);
            await WaitUntilAsync(() => server.ActiveTarget is not null);
        }
        await WaitUntilAsync(() => server.ActiveTarget is null);

        var waitTask = server.WaitForActiveTargetAsync(
            TimeSpan.FromSeconds(2),
            CancellationToken.None);
        await Task.Delay(25);

        await using var client = CreateClient(pipeName);
        await client.ConnectAsync(2_000, CancellationToken.None);
        var expected = new IpcSessionId(4, 8);
        await NamedPipeFrameProtocol.WriteAsync(
            client,
            Hello(expected, 12),
            CancellationToken.None);

        var target = await waitTask;
        AssertEx.True(target is not null, "Reconnect gap timed out without an active target.");
        AssertEx.Equal(expected, target!.SessionId);
        AssertEx.Equal<ulong>(12, target.FocusGeneration);
    });

    [KeyinaTest("newer hello becomes active and disconnected clients are removed")]
    private static void ActiveTargetTracksConnectionLifecycle() => Run(async () =>
    {
        var pipeName = $"Keyina.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeEnvelopeServer(pipeName);
        await server.StartAsync(CancellationToken.None);

        await using var first = CreateClient(pipeName);
        await first.ConnectAsync(2_000, CancellationToken.None);
        await NamedPipeFrameProtocol.WriteAsync(
            first,
            Hello(new IpcSessionId(1, 1), 1),
            CancellationToken.None);
        await WaitUntilAsync(() => server.ActiveTarget?.SessionId == new IpcSessionId(1, 1));

        await using var second = CreateClient(pipeName);
        await second.ConnectAsync(2_000, CancellationToken.None);
        await NamedPipeFrameProtocol.WriteAsync(
            second,
            Hello(new IpcSessionId(2, 2), 5),
            CancellationToken.None);
        await WaitUntilAsync(() => server.ActiveTarget?.SessionId == new IpcSessionId(2, 2));

        second.Dispose();
        await WaitUntilAsync(() => server.ActiveTarget?.SessionId == new IpcSessionId(1, 1));
        first.Dispose();
        await WaitUntilAsync(() => server.ActiveTarget is null);
        AssertEx.Equal(
            EnvelopeRouteResult.NoActiveTarget,
            await server.WriteToActiveAsync(
                new IpcEnvelope(
                    IpcMessageType.FinalTranscript,
                    0,
                    new IpcSessionId(1, 1),
                    1,
                    "text"),
                CancellationToken.None));
    });

    [KeyinaTest("named pipe server rejects a non-Hello first frame")]
    private static void FirstFrameMustBeHello() => Run(async () =>
    {
        var pipeName = $"Keyina.Tests.{Guid.NewGuid():N}";
        await using var server = new NamedPipeEnvelopeServer(pipeName);
        await server.StartAsync(CancellationToken.None);
        await using var client = CreateClient(pipeName);
        await client.ConnectAsync(2_000, CancellationToken.None);

        await NamedPipeFrameProtocol.WriteAsync(
            client,
            new IpcEnvelope(
                IpcMessageType.FinalTranscript,
                0,
                new IpcSessionId(9, 9),
                1,
                "not hello"),
            CancellationToken.None);
        await WaitUntilAsync(() => server.RejectedConnectionCount == 1);
        AssertEx.Equal<ActivePipeTarget?>(null, server.ActiveTarget);
    });

    private static NamedPipeClientStream CreateClient(string pipeName) =>
        new(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

    private static IpcEnvelope Hello(IpcSessionId sessionId, ulong generation) =>
        new(
            IpcMessageType.Hello,
            0,
            sessionId,
            generation,
            "pid=1;tid=1;cap=external_text");

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!condition())
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static void Run(Func<Task> action) => action().GetAwaiter().GetResult();

    private sealed class FragmentedReadStream(byte[] data, int maximumChunk) : Stream
    {
        private int offset;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => data.Length;
        public override long Position { get => offset; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int bufferOffset, int count) =>
            throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            if (offset >= data.Length)
            {
                return 0;
            }

            var count = Math.Min(Math.Min(maximumChunk, buffer.Length), data.Length - offset);
            data.AsSpan(offset, count).CopyTo(buffer.Span);
            offset += count;
            return count;
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
