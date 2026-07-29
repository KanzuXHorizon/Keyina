using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using Keyina.Host.Core.Ipc;

namespace Keyina.Host.Windows.Ipc;

public enum EnvelopeRouteResult
{
    Written,
    NoActiveTarget,
    StaleTarget,
    Disconnected,
}

public sealed record ActivePipeTarget(
    IpcSessionId SessionId,
    ulong FocusGeneration,
    long ConnectionId);

public static class PipeEndpointName
{
    public static string ForCurrentSession()
    {
        using var process = Process.GetCurrentProcess();
        return $"Keyina.Host.v1.s{process.SessionId}";
    }
}

public sealed class NamedPipeEnvelopeServer : IAsyncDisposable
{
    private const int MaximumServerInstances = 16;
    private const int PipeBufferBytes = IpcFrameCodec.MaximumFrameBytes;

    private readonly string pipeName;
    private readonly CancellationTokenSource shutdown = new();
    private readonly ConcurrentDictionary<long, ClientConnection> connections = new();
    private readonly object targetGate = new();
    private Task? acceptLoop;
    private ActivePipeTarget? activeTarget;
    private long nextConnectionId;
    private long activitySequence;
    private int rejectedConnectionCount;
    private bool disposed;

    public NamedPipeEnvelopeServer(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        if (pipeName.Length > 240 ||
            pipeName.IndexOfAny(['\\', '/', ':']) >= 0)
        {
            throw new ArgumentException("Named-pipe name must be a short local identifier.", nameof(pipeName));
        }
        this.pipeName = pipeName;
    }

    public ActivePipeTarget? ActiveTarget
    {
        get
        {
            lock (targetGate)
            {
                return activeTarget;
            }
        }
    }

    public int RejectedConnectionCount => Volatile.Read(ref rejectedConnectionCount);

    public Task StartAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.CompareExchange(
                ref acceptLoop,
                Task.CompletedTask,
                comparand: null) is not null)
        {
            throw new InvalidOperationException("Named-pipe server is already started.");
        }

        acceptLoop = AcceptLoopAsync(shutdown.Token);
        return Task.CompletedTask;
    }

    public async ValueTask<EnvelopeRouteResult> WriteToActiveAsync(
        IpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var target = ActiveTarget;
        if (target is null)
        {
            return EnvelopeRouteResult.NoActiveTarget;
        }

        if (target.SessionId != envelope.SessionId ||
            target.FocusGeneration != envelope.FocusGeneration)
        {
            return EnvelopeRouteResult.StaleTarget;
        }

        if (!connections.TryGetValue(target.ConnectionId, out var connection))
        {
            return EnvelopeRouteResult.Disconnected;
        }

        return await connection.WriteAsync(
            envelope,
            target,
            cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        shutdown.Cancel();

        foreach (var connection in connections.Values)
        {
            connection.Dispose();
        }

        if (acceptLoop is not null)
        {
            try
            {
                await acceptLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        var connectionTasks = connections.Values
            .Select(connection => connection.Completion)
            .ToArray();
        if (connectionTasks.Length != 0)
        {
            try
            {
                await Task.WhenAll(connectionTasks).ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is EndOfStreamException or IOException or OperationCanceledException)
            {
            }
        }

        connections.Clear();
        lock (targetGate)
        {
            activeTarget = null;
        }
        shutdown.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var pipe = CreateServerPipe();
            try
            {
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                pipe.Dispose();
                throw;
            }

            var connectionId = Interlocked.Increment(ref nextConnectionId);
            var connection = new ClientConnection(this, connectionId, pipe);
            if (!connections.TryAdd(connectionId, connection))
            {
                pipe.Dispose();
                continue;
            }
            connection.Start(cancellationToken);
        }
    }

    private NamedPipeServerStream CreateServerPipe() =>
        new(
            pipeName,
            PipeDirection.InOut,
            MaximumServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
            PipeBufferBytes,
            PipeBufferBytes);

    private void UpdateTarget(ClientConnection connection, IpcEnvelope hello)
    {
        connection.SessionId = hello.SessionId;
        connection.FocusGeneration = hello.FocusGeneration;
        connection.ActivitySequence = Interlocked.Increment(ref activitySequence);
        lock (targetGate)
        {
            activeTarget = new ActivePipeTarget(
                connection.SessionId,
                connection.FocusGeneration,
                connection.ConnectionId);
        }
    }

    private void Reject(ClientConnection connection)
    {
        Interlocked.Increment(ref rejectedConnectionCount);
        connection.Dispose();
    }

    private void Remove(ClientConnection connection)
    {
        connections.TryRemove(connection.ConnectionId, out _);
        lock (targetGate)
        {
            if (activeTarget?.ConnectionId != connection.ConnectionId)
            {
                return;
            }

            var fallback = connections.Values
                .Where(candidate => candidate.HasHello)
                .OrderByDescending(candidate => candidate.ActivitySequence)
                .FirstOrDefault();
            activeTarget = fallback is null
                ? null
                : new ActivePipeTarget(
                    fallback.SessionId,
                    fallback.FocusGeneration,
                    fallback.ConnectionId);
        }
    }

    private sealed class ClientConnection : IDisposable
    {
        private readonly NamedPipeEnvelopeServer owner;
        private readonly NamedPipeServerStream pipe;
        private readonly SemaphoreSlim writeGate = new(1, 1);
        private CancellationTokenSource? connectionCancellation;
        private bool disposed;

        public ClientConnection(
            NamedPipeEnvelopeServer owner,
            long connectionId,
            NamedPipeServerStream pipe)
        {
            this.owner = owner;
            ConnectionId = connectionId;
            this.pipe = pipe;
        }

        public long ConnectionId { get; }
        public IpcSessionId SessionId { get; set; }
        public ulong FocusGeneration { get; set; }
        public long ActivitySequence { get; set; }
        public bool HasHello { get; private set; }
        public Task Completion { get; private set; } = Task.CompletedTask;

        public void Start(CancellationToken serverCancellation)
        {
            connectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                serverCancellation);
            Completion = RunAsync(connectionCancellation.Token);
        }

        public async ValueTask<EnvelopeRouteResult> WriteAsync(
            IpcEnvelope envelope,
            ActivePipeTarget expectedTarget,
            CancellationToken cancellationToken)
        {
            if (disposed)
            {
                return EnvelopeRouteResult.Disconnected;
            }

            await writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (disposed || !pipe.IsConnected)
                {
                    return EnvelopeRouteResult.Disconnected;
                }
                if (SessionId != expectedTarget.SessionId ||
                    FocusGeneration != expectedTarget.FocusGeneration)
                {
                    return EnvelopeRouteResult.StaleTarget;
                }

                await NamedPipeFrameProtocol.WriteAsync(
                    pipe,
                    envelope,
                    cancellationToken).ConfigureAwait(false);
                return EnvelopeRouteResult.Written;
            }
            catch (IOException)
            {
                return EnvelopeRouteResult.Disconnected;
            }
            finally
            {
                writeGate.Release();
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            connectionCancellation?.Cancel();
            pipe.Dispose();
            writeGate.Dispose();
            connectionCancellation?.Dispose();
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            try
            {
                var first = await NamedPipeFrameProtocol.ReadAsync(
                    pipe,
                    cancellationToken).ConfigureAwait(false);
                if (first.MessageType != IpcMessageType.Hello)
                {
                    owner.Reject(this);
                    return;
                }

                HasHello = true;
                owner.UpdateTarget(this, first);
                while (!cancellationToken.IsCancellationRequested)
                {
                    var envelope = await NamedPipeFrameProtocol.ReadAsync(
                        pipe,
                        cancellationToken).ConfigureAwait(false);
                    if (envelope.MessageType != IpcMessageType.Hello ||
                        envelope.SessionId != SessionId)
                    {
                        owner.Reject(this);
                        return;
                    }
                    owner.UpdateTarget(this, envelope);
                }
            }
            catch (Exception exception) when (
                exception is EndOfStreamException or IOException or OperationCanceledException or PipeProtocolException)
            {
            }
            finally
            {
                owner.Remove(this);
                Dispose();
            }
        }
    }
}
