using System.Text;

namespace Keyina.Host.Core.Snippets;

public enum SnippetCommand
{
    None,
    ToggleVietnamese,
    ToggleDictation,
    ExternalOutput,
}

public sealed record SnippetDefinition(
    string Trigger,
    string Expansion,
    bool CaseSensitive,
    bool PreserveDelimiter,
    IReadOnlySet<char> Delimiters,
    IReadOnlySet<string> AllowedApplications,
    IReadOnlySet<string> ExcludedApplications,
    SnippetCommand Command = SnippetCommand.None)
{
    public const int MaximumTriggerCodePoints = 64;
    public const int MaximumExpansionUtf8Bytes = 16 * 1024;

    internal int Validate()
    {
        if (string.IsNullOrEmpty(Trigger))
        {
            throw new ArgumentException("Snippet trigger must not be empty.", nameof(Trigger));
        }
        if (Trigger.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("Snippet trigger must not contain whitespace.", nameof(Trigger));
        }

        var firstRune = Trigger.EnumerateRunes().First();
        if (Rune.IsLetterOrDigit(firstRune))
        {
            throw new ArgumentException(
                "Snippet trigger must begin with a non-alphanumeric prefix such as ';'.",
                nameof(Trigger));
        }

        var triggerCodePoints = Trigger.EnumerateRunes().Count();
        if (triggerCodePoints > MaximumTriggerCodePoints)
        {
            throw new ArgumentException(
                $"Snippet trigger exceeds {MaximumTriggerCodePoints} Unicode code points.",
                nameof(Trigger));
        }
        if (Encoding.UTF8.GetByteCount(Expansion) > MaximumExpansionUtf8Bytes)
        {
            throw new ArgumentException(
                $"Snippet expansion exceeds {MaximumExpansionUtf8Bytes} UTF-8 bytes.",
                nameof(Expansion));
        }
        if (Delimiters is null || Delimiters.Count == 0)
        {
            throw new ArgumentException("Snippet must allow at least one delimiter.", nameof(Delimiters));
        }
        if (AllowedApplications is null || ExcludedApplications is null)
        {
            throw new ArgumentException("Snippet application scopes must not be null.");
        }
        if (AllowedApplications.Any(string.IsNullOrWhiteSpace) ||
            ExcludedApplications.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Snippet application identifiers must not be empty.");
        }
        if (Command is SnippetCommand.ToggleVietnamese or SnippetCommand.ToggleDictation &&
            Expansion.Length != 0)
        {
            throw new ArgumentException("Built-in command snippets must not also insert text.", nameof(Expansion));
        }
        if (Command is SnippetCommand.None or SnippetCommand.ExternalOutput && Expansion.Length == 0)
        {
            throw new ArgumentException("Text and external-output snippets must provide a payload.", nameof(Expansion));
        }

        if (Command == SnippetCommand.None)
        {
            SnippetVariableExpander.Validate(Expansion);
        }
        return triggerCodePoints;
    }
}

public sealed record SnippetMatch(
    int EraseCodePoints,
    string InsertText,
    bool PreserveDelimiter,
    SnippetCommand Command);
