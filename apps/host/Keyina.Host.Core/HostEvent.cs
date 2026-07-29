namespace Keyina.Host.Core;

public abstract record HostEvent;

public sealed record InputModeChanged(bool Enabled) : HostEvent;

public sealed record ListeningStarted : HostEvent;

public sealed record ListeningStopped : HostEvent;

public sealed record HostFailed(string ErrorCode) : HostEvent;

public sealed record HostRecovered : HostEvent;
