using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Keyina.Host.Core.Translation;

public static class TranslationTextProtector
{
    private static readonly Regex TechnicalTokenPattern = new(
        """
        (?:
            ```[\s\S]*?```
          | `[^`\r\n]+`
          | </?[A-Za-z][^>\r\n]*>
          | https?://[^\s<>"']+
          | www\.[^\s<>"']+
          | [A-Za-z0-9.!#$%&'*+/=?^_`{|}~-]+@[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+
          | \b[A-Za-z]:\\[^\s<>"|]+
          | \\\\[A-Za-z0-9_.-]+\\[^\s<>"|]+
          | \{\{[^{}\r\n]+\}\}
          | \$\{[^{}\r\n]+\}
          | \{[A-Za-z_][A-Za-z0-9_.-]*\}
          | %(?:\d+\$)?[sdif]
          | --[A-Za-z0-9][A-Za-z0-9_-]*
          | \$[A-Za-z_][A-Za-z0-9_]*
          | \b[A-Za-z_][A-Za-z0-9_.]*\([^()\r\n]*\)
          | \b[A-Za-z_][A-Za-z0-9_-]*(?:[./\\][A-Za-z0-9_.-]+)+
        )
        """,
        RegexOptions.Compiled |
        RegexOptions.CultureInvariant |
        RegexOptions.IgnorePatternWhitespace,
        TimeSpan.FromMilliseconds(100));

    public static ProtectedTranslationText Protect(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var matches = TechnicalTokenPattern.Matches(text);
        if (matches.Count == 0)
        {
            return ProtectedTranslationText.Plain(
                text,
                ContainsTranslatableText(text));
        }

        var tokens = new string[matches.Count];
        var payload = new StringBuilder(text.Length + (matches.Count * 32));
        var settings = new XmlWriterSettings
        {
            ConformanceLevel = ConformanceLevel.Document,
            OmitXmlDeclaration = true,
            NewLineHandling = NewLineHandling.None,
        };
        var hasTranslatableContent = false;
        using (var writer = XmlWriter.Create(payload, settings))
        {
            writer.WriteStartElement("root");
            var cursor = 0;
            for (var index = 0; index < matches.Count; index++)
            {
                var match = matches[index];
                var naturalText = text.AsSpan(cursor, match.Index - cursor);
                if (!naturalText.IsEmpty)
                {
                    writer.WriteString(naturalText.ToString());
                    hasTranslatableContent |= ContainsTranslatableText(naturalText);
                }

                tokens[index] = match.Value;
                writer.WriteStartElement("keep");
                writer.WriteAttributeString(
                    "id",
                    index.ToString(CultureInfo.InvariantCulture));
                writer.WriteString(match.Value);
                writer.WriteEndElement();
                cursor = match.Index + match.Length;
            }

            var trailingText = text.AsSpan(cursor);
            if (!trailingText.IsEmpty)
            {
                writer.WriteString(trailingText.ToString());
                hasTranslatableContent |= ContainsTranslatableText(trailingText);
            }
            writer.WriteEndElement();
        }

        return ProtectedTranslationText.Xml(
            payload.ToString(),
            tokens,
            hasTranslatableContent);
    }

    private static bool ContainsTranslatableText(string text) =>
        ContainsTranslatableText(text.AsSpan());

    private static bool ContainsTranslatableText(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (char.IsLetter(character))
            {
                return true;
            }
        }
        return false;
    }
}

public sealed class ProtectedTranslationText
{
    private readonly string[] protectedTokens;

    private ProtectedTranslationText(
        string payload,
        string[] protectedTokens,
        bool usesXmlTagHandling,
        bool hasTranslatableContent)
    {
        Payload = payload;
        this.protectedTokens = protectedTokens;
        UsesXmlTagHandling = usesXmlTagHandling;
        HasTranslatableContent = hasTranslatableContent;
    }

    public string Payload { get; }

    public bool UsesXmlTagHandling { get; }

    public bool HasTranslatableContent { get; }

    internal static ProtectedTranslationText Plain(
        string payload,
        bool hasTranslatableContent) =>
        new(payload, [], usesXmlTagHandling: false, hasTranslatableContent);

    internal static ProtectedTranslationText Xml(
        string payload,
        string[] protectedTokens,
        bool hasTranslatableContent) =>
        new(payload, protectedTokens, usesXmlTagHandling: true, hasTranslatableContent);

    public string Restore(string translatedPayload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(translatedPayload);
        if (!UsesXmlTagHandling)
        {
            return translatedPayload;
        }

        try
        {
            var readerSettings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 256 * 1024,
            };
            using var textReader = new StringReader(translatedPayload);
            using var xmlReader = XmlReader.Create(textReader, readerSettings);
            var document = XDocument.Load(
                xmlReader,
                LoadOptions.PreserveWhitespace);
            var root = document.Root;
            if (root is null ||
                root.Name.Namespace != XNamespace.None ||
                !string.Equals(root.Name.LocalName, "root", StringComparison.Ordinal))
            {
                throw InvalidProtectedResponse();
            }

            var restored = new StringBuilder(translatedPayload.Length);
            var seen = new bool[protectedTokens.Length];
            foreach (var node in root.Nodes())
            {
                switch (node)
                {
                    case XCData cdata:
                        restored.Append(cdata.Value);
                        break;
                    case XText text:
                        restored.Append(text.Value);
                        break;
                    case XElement element:
                        AppendProtectedToken(element, seen, restored);
                        break;
                    default:
                        throw InvalidProtectedResponse();
                }
            }

            if (seen.Any(value => !value))
            {
                throw InvalidProtectedResponse();
            }
            return restored.ToString();
        }
        catch (TranslationException)
        {
            throw;
        }
        catch (Exception)
        {
            throw InvalidProtectedResponse();
        }
    }

    private void AppendProtectedToken(
        XElement element,
        bool[] seen,
        StringBuilder restored)
    {
        if (element.Name.Namespace != XNamespace.None ||
            !string.Equals(element.Name.LocalName, "keep", StringComparison.Ordinal) ||
            element.Attributes().Count() != 1 ||
            element.Attribute("id") is not { Value: var idText } ||
            !int.TryParse(
                idText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var id) ||
            id < 0 ||
            id >= protectedTokens.Length ||
            seen[id])
        {
            throw InvalidProtectedResponse();
        }

        seen[id] = true;
        restored.Append(protectedTokens[id]);
    }

    private static TranslationException InvalidProtectedResponse() =>
        new(
            TranslationFailureCode.InvalidResponse,
            "The translation provider changed protected technical content.");
}
