using Keyina.Host.Core.Ipc;
using Keyina.Host.Speech;
using Keyina.Host.Windows.Ipc;
using Keyina.Host.Windows.Typing;

namespace Keyina.Host.Runtime;

public sealed class ActivePipeEnvelopeWriter : IIpcEnvelopeWriter
{
    private readonly NamedPipeEnvelopeServer server;
    private readonly IUnicodeInputInjector injector;
    private readonly Func<VietnameseTypingContext> contextProvider;
    private readonly object fallbackGate = new();
    private VietnameseTypingContext? fallbackContext;
    private IpcSessionId fallbackSessionId;
    private ulong fallbackGeneration;
    private ulong nextFallbackSession;

    public ActivePipeEnvelopeWriter(
        NamedPipeEnvelopeServer server,
        IUnicodeInputInjector? injector = null,
        Func<VietnameseTypingContext>? contextProvider = null)
    {
        this.server = server ?? throw new ArgumentNullException(nameof(server));
        this.injector = injector ?? new UnicodeInputInjector();
        this.contextProvider = contextProvider ?? WindowsTypingContextProbe.Capture;
    }

    public ulong CurrentFocusGeneration
    {
        get
        {
            var active = server.ActiveTarget;
            if (active is not null)
            {
                return active.FocusGeneration;
            }
            lock (fallbackGate)
            {
                return fallbackGeneration;
            }
        }
    }

    public async Task<ActivePipeTarget?> CaptureTargetAsync(
        TimeSpan reconnectGrace,
        CancellationToken cancellationToken)
    {
        var target = server.ActiveTarget;
        if (target is null)
        {
            target = await server.WaitForActiveTargetAsync(
                    reconnectGrace,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        if (target is not null)
        {
            ClearFallback();
            return target;
        }

        var context = contextProvider();
        if (context.ForegroundProcessId == 0 ||
            context.FocusWindow == 0 ||
            context.ShouldBypassTyping)
        {
            ClearFallback();
            return null;
        }

        lock (fallbackGate)
        {
            fallbackContext = context;
            fallbackSessionId = new IpcSessionId(
                checked((ulong)Environment.ProcessId),
                ++nextFallbackSession);
            fallbackGeneration++;
            if (fallbackGeneration == 0)
            {
                fallbackGeneration = 1;
            }
            return new ActivePipeTarget(
                fallbackSessionId,
                fallbackGeneration,
                ConnectionId: 0);
        }
    }

    public async ValueTask WriteAsync(
        IpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var result = await server.WriteToActiveAsync(envelope, cancellationToken)
            .ConfigureAwait(false);
        if (result == EnvelopeRouteResult.Written)
        {
            return;
        }

        VietnameseTypingContext? captured;
        IpcSessionId sessionId;
        ulong generation;
        lock (fallbackGate)
        {
            captured = fallbackContext;
            sessionId = fallbackSessionId;
            generation = fallbackGeneration;
        }

        if (captured is null ||
            envelope.SessionId != sessionId ||
            envelope.FocusGeneration != generation)
        {
            throw new IpcDeliveryException(result);
        }

        var current = contextProvider();
        if (current.ShouldBypassTyping ||
            current.ForegroundProcessId != captured.Value.ForegroundProcessId ||
            current.FocusWindow != captured.Value.FocusWindow)
        {
            throw new IpcDeliveryException(EnvelopeRouteResult.StaleTarget);
        }

        injector.Apply(new HookEdit(
            BackspaceCount: 0,
            InsertText: envelope.Payload,
            ConsumePhysicalKey: true));
    }

    private void ClearFallback()
    {
        lock (fallbackGate)
        {
            fallbackContext = null;
            fallbackSessionId = default;
            fallbackGeneration = 0;
        }
    }
}

public sealed class IpcDeliveryException(EnvelopeRouteResult result)
    : IOException($"Focused text delivery failed: {result}.")
{
    public EnvelopeRouteResult Result { get; } = result;
}
