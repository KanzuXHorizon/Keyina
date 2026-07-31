using Keyina.Host.Core.Ipc;
using Keyina.Host.Speech;
using Keyina.Host.Windows.Typing;

namespace Keyina.Host.Runtime;

public sealed class FocusedUnicodeDeliveryException(string message)
    : IOException(message);

public sealed class FocusedUnicodeEnvelopeWriter : IIpcEnvelopeWriter
{
    private readonly Func<VietnameseTypingContext> captureContext;
    private readonly Action<string> insertText;

    private VietnameseTypingContext target;
    private IpcSessionId sessionId;
    private ulong focusGeneration;
    private bool bound;

    public FocusedUnicodeEnvelopeWriter()
        : this(
            WindowsTypingContextProbe.Capture,
            text => new UnicodeInputInjector().Apply(
                new HookEdit(0, text, ConsumePhysicalKey: true)))
    {
    }

    public FocusedUnicodeEnvelopeWriter(
        Func<VietnameseTypingContext> captureContext,
        Action<string> insertText)
    {
        this.captureContext = captureContext ??
            throw new ArgumentNullException(nameof(captureContext));
        this.insertText = insertText ??
            throw new ArgumentNullException(nameof(insertText));
    }

    public IpcSessionId SessionId => sessionId;

    public ulong FocusGeneration => focusGeneration;

    public bool TryBindToCurrentFocus() => TryBindToExpectedFocus(null, null);

    public bool TryBindToExpectedFocus(int? foregroundProcessId, nint? focusWindow)
    {
        var current = captureContext();
        if (current.ForegroundProcessId <= 0 ||
            current.FocusWindow == 0 ||
            current.ShouldBypassTyping ||
            (foregroundProcessId is not null && current.ForegroundProcessId != foregroundProcessId.Value) ||
            (focusWindow is not null && current.FocusWindow != focusWindow.Value))
        {
            bound = false;
            return false;
        }

        target = current;
        sessionId = IpcSessionId.New();
        focusGeneration = checked(focusGeneration + 1);
        bound = true;
        return true;
    }

    public ValueTask WriteAsync(
        IpcEnvelope envelope,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(envelope);
        if (!bound ||
            envelope.SessionId != sessionId ||
            envelope.FocusGeneration != focusGeneration)
        {
            throw new FocusedUnicodeDeliveryException(
                "Dictation target session is stale.");
        }

        var current = captureContext();
        if (current.ShouldBypassTyping ||
            current.ForegroundProcessId != target.ForegroundProcessId ||
            current.FocusWindow != target.FocusWindow)
        {
            bound = false;
            throw new FocusedUnicodeDeliveryException(
                "Focused control changed before dictation delivery.");
        }
        if (string.IsNullOrEmpty(envelope.Payload))
        {
            throw new FocusedUnicodeDeliveryException(
                "Dictation payload was empty.");
        }

        insertText(envelope.Payload);
        return ValueTask.CompletedTask;
    }
}
