namespace Keyina.Host.Core.Ipc;

public enum IpcMessageType : ushort
{
    Hello = 1,
    BeginDictation = 2,
    PartialTranscript = 3,
    FinalTranscript = 4,
    EndDictation = 5,
    ToggleInput = 6,
    ConfigurationChanged = 7,
    SnippetExpansion = 8,
}

public enum IpcDecodeStatus
{
    Success,
    NeedMoreData,
    Invalid,
}

public enum IpcDecodeError
{
    None,
    InvalidMagic,
    UnsupportedVersion,
    UnknownMessageType,
    FrameTooLarge,
    InvalidUtf8,
}
