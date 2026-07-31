using Keyina.Host.Core.Hotkeys;
using Keyina.Host.Core.Snippets;

namespace Keyina.Host.Tests;

internal static class SnippetMatcherTests
{
    private static readonly IReadOnlySet<char> SpaceDelimiter = new HashSet<char> { ' ' };

    [KeyinaTest("snippet matches an exact trigger and preserves configured delimiter")]
    private static void ExactTriggerMatches()
    {
        var matcher = new SnippetMatcher(
        [
            Definition(";mail", "hello@example.com", preserveDelimiter: true),
        ]);

        var match = matcher.Match(";mail", ' ', Context());
        AssertEx.NotNull(match, "Expected snippet match.");
        AssertEx.Equal(5, match!.EraseCodePoints);
        AssertEx.Equal("hello@example.com", match.InsertText);
        AssertEx.True(match.PreserveDelimiter, "Delimiter should be preserved.");
        AssertEx.Equal(SnippetCommand.None, match.Command);
    }

    [KeyinaTest("snippet requires an explicitly allowed delimiter")]
    private static void DelimiterMustBeAllowed()
    {
        var matcher = new SnippetMatcher([Definition(";mail", "value")]);
        AssertEx.Equal<SnippetMatch?>(null, matcher.Match(";mail", '\t', Context()));
    }

    [KeyinaTest("snippet case sensitivity is enforced per definition")]
    private static void CasePolicyIsEnforced()
    {
        var sensitive = new SnippetMatcher([Definition(";Case", "sensitive", caseSensitive: true)]);
        AssertEx.NotNull(sensitive.Match(";Case", ' ', Context()), "Exact case should match.");
        AssertEx.Equal<SnippetMatch?>(null, sensitive.Match(";case", ' ', Context()));

        var insensitive = new SnippetMatcher([Definition(";Case", "insensitive", caseSensitive: false)]);
        AssertEx.NotNull(insensitive.Match(";case", ' ', Context()), "Case-insensitive trigger did not match.");
    }

    [KeyinaTest("secure input always disables snippet expansion")]
    private static void SecureInputIsExcluded()
    {
        var matcher = new SnippetMatcher([Definition(";mail", "secret")]);
        var context = Context() with { SecureInput = true };
        AssertEx.Equal<SnippetMatch?>(null, matcher.Match(";mail", ' ', context));
    }

    [KeyinaTest("application deny scope wins and allow scope is respected")]
    private static void ApplicationScopesAreRespected()
    {
        var matcher = new SnippetMatcher(
        [
            Definition(
                ";mail",
                "value",
                allowed: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "notepad.exe", "code.exe" },
                excluded: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "code.exe" }),
        ]);

        AssertEx.NotNull(matcher.Match(";mail", ' ', Context("NOTEPAD.EXE")), "Allowed app did not match.");
        AssertEx.Equal<SnippetMatch?>(null, matcher.Match(";mail", ' ', Context("code.exe")));
        AssertEx.Equal<SnippetMatch?>(null, matcher.Match(";mail", ' ', Context("word.exe")));
    }

    [KeyinaTest("snippet validation rejects duplicate and oversized definitions")]
    private static void InvalidDefinitionsAreRejected()
    {
        AssertThrows<ArgumentException>(() => _ = new SnippetMatcher(
        [
            Definition(";Mail", "one", caseSensitive: true),
            Definition(";mail", "two", caseSensitive: false),
        ]));

        AssertThrows<ArgumentException>(() => _ = new SnippetMatcher(
        [
            Definition("mail", "missing prefix"),
        ]));

        AssertThrows<ArgumentException>(() => _ = new SnippetMatcher(
        [
            Definition(new string('a', 65), "too long"),
        ]));

        AssertThrows<ArgumentException>(() => _ = new SnippetMatcher(
        [
            Definition(";large", new string('x', (16 * 1024) + 1)),
        ]));
    }

    [KeyinaTest("Unicode triggers report Unicode code point erasure length")]
    private static void UnicodeTriggerUsesCodePoints()
    {
        var matcher = new SnippetMatcher([Definition(";🙂", "emoji")]);
        var match = matcher.Match(";🙂", ' ', Context());
        AssertEx.NotNull(match, "Unicode trigger did not match.");
        AssertEx.Equal(2, match!.EraseCodePoints);
    }

    [KeyinaTest("built in Keyina snippets expose commands and deterministic date time")]
    private static void BuiltInsAreDeterministic()
    {
        var matcher = new SnippetMatcher(BuiltInSnippets.Create());
        var now = new DateTimeOffset(2026, 7, 29, 18, 32, 45, TimeSpan.FromHours(7));
        var context = new SnippetContext("notepad.exe", SecureInput: false, now);

        AssertEx.Equal(
            SnippetCommand.ToggleVietnamese,
            matcher.Match(";kvi", ' ', context)!.Command);
        AssertEx.Equal(
            SnippetCommand.ToggleDictation,
            matcher.Match(";kvoice", ' ', context)!.Command);
        AssertEx.Equal("2026-07-29", matcher.Match(";kdate", ' ', context)!.InsertText);
        AssertEx.Equal("18:32", matcher.Match(";ktime", ' ', context)!.InsertText);
        AssertEx.Equal("2026-07-29 18:32", matcher.Match(";kdatetime", ' ', context)!.InsertText);
    }

    [KeyinaTest("snippet variable expansion rejects unknown variables")]
    private static void UnknownVariablesAreRejected()
    {
        AssertThrows<ArgumentException>(() => _ = new SnippetMatcher(
        [
            Definition(";bad", "${unknown}"),
        ]));
    }

    private static SnippetDefinition Definition(
        string trigger,
        string expansion,
        bool caseSensitive = true,
        bool preserveDelimiter = false,
        IReadOnlySet<string>? allowed = null,
        IReadOnlySet<string>? excluded = null,
        SnippetCommand command = SnippetCommand.None) =>
        new(
            trigger,
            expansion,
            caseSensitive,
            preserveDelimiter,
            SpaceDelimiter,
            allowed ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            excluded ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            command);

    private static SnippetContext Context(string application = "notepad.exe") =>
        new(
            application,
            SecureInput: false,
            new DateTimeOffset(2026, 7, 29, 18, 32, 0, TimeSpan.FromHours(7)));

    private static void AssertThrows<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
