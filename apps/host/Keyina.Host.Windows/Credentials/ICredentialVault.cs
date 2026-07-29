namespace Keyina.Host.Windows.Credentials;

public interface ICredentialVault
{
    void Write(string target, string secret);

    string? Read(string target);

    bool Delete(string target);
}

public static class CredentialTargets
{
    public const string SpeechmaticsApiKey = "Keyina/Speechmatics/ApiKey";
}
