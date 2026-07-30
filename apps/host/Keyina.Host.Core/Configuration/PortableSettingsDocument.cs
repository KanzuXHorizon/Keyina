namespace Keyina.Host.Core.Configuration;

public sealed record PortableSettingsDocument(
    int FormatVersion,
    KeyinaConfiguration Configuration)
{
    public const int CurrentFormatVersion = 1;

    public void Validate()
    {
        if (FormatVersion != CurrentFormatVersion)
        {
            throw new ConfigurationValidationException(
                $"Unsupported portable settings format version: {FormatVersion}.");
        }
        if (Configuration is null)
        {
            throw new ConfigurationValidationException(
                "Portable settings configuration is missing.");
        }
        _ = Configuration.ValidateAndCreateSnippets();
    }
}
