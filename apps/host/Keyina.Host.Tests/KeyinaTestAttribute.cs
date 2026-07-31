namespace Keyina.Host.Tests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
internal sealed class KeyinaTestAttribute : Attribute
{
    public KeyinaTestAttribute(string name)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Test name must not be empty.", nameof(name))
            : name;
    }

    public string Name { get; }
}

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method,
    AllowMultiple = false,
    Inherited = false)]
internal sealed class KeyinaInteractiveTestAttribute : Attribute;
