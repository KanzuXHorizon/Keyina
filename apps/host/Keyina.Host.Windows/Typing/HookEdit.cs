namespace Keyina.Host.Windows.Typing;

public readonly record struct HookEdit(
    int BackspaceCount,
    string InsertText,
    bool ConsumePhysicalKey)
{
    public static HookEdit PassThrough { get; } = new(0, string.Empty, false);
}
