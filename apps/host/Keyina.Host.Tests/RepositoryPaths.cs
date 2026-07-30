namespace Keyina.Host.Tests;

internal static class RepositoryPaths
{
    public static string Root { get; } = FindRoot();

    private static string FindRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var gitPath = Path.Combine(current.FullName, ".git");
            if (File.Exists(Path.Combine(current.FullName, "Keyina.slnx")) &&
                (Directory.Exists(gitPath) || File.Exists(gitPath)))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate the Keyina repository from {AppContext.BaseDirectory}.");
    }
}
