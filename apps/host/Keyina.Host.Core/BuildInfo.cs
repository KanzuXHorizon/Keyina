using System.Reflection;

namespace Keyina.Host.Core;

public static class BuildInfo
{
    public const string ProductName = "Keyina";

    public static string ProductVersion { get; } = ResolveProductVersion();

    public const int ProtocolVersion = 1;

    private static string ResolveProductVersion()
    {
        var informationalVersion = typeof(BuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (string.IsNullOrWhiteSpace(informationalVersion))
        {
            return "0.0.0";
        }

        var metadataSeparator = informationalVersion.IndexOf('+', StringComparison.Ordinal);
        return metadataSeparator < 0
            ? informationalVersion
            : informationalVersion[..metadataSeparator];
    }
}
