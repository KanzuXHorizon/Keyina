using Keyina.Host.Core.Ipc;
using Keyina.Host.Speech;
using Keyina.Host.Windows.Ipc;

namespace Keyina.Host.Runtime;

public sealed class ActivePipeEnvelopeWriter(
    NamedPipeEnvelopeServer server) : IIpcEnvelopeWriter
{
    private readonly NamedPipeEnvelopeServer server =
        server ?? throw new ArgumentNullException(nameof(server));

    public async ValueTask WriteAsync(
        IpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var result = await server.WriteToActiveAsync(envelope, cancellationToken)
            .ConfigureAwait(false);
        if (result != EnvelopeRouteResult.Written)
        {
            throw new IpcDeliveryException(result);
        }
    }
}

public sealed class IpcDeliveryException(EnvelopeRouteResult result)
    : IOException($"Focused TSF IPC delivery failed: {result}.")
{
    public EnvelopeRouteResult Result { get; } = result;
}
