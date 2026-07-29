namespace Keyina.Host.Core.Snippets;

public readonly record struct SnippetContext(
    string ApplicationId,
    bool SecureInput,
    DateTimeOffset Now)
{
    public string NormalizedApplicationId => ApplicationId.Trim();
}
