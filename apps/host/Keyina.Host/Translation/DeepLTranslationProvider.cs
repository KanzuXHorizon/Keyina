using System.Net;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Keyina.Host.Core.Translation;

namespace Keyina.Host.Translation;

public sealed class DeepLTranslationProvider : ITranslationProvider
{
    private const int MaximumRequestBytes = 128 * 1024;
    private const int MaximumResponseBytes = 256 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    private static readonly Uri FreeEndpoint = new("https://api-free.deepl.com/v2/translate");
    private static readonly Uri ProEndpoint = new("https://api.deepl.com/v2/translate");
    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    private readonly HttpClient httpClient;

    public DeepLTranslationProvider(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<TranslationResult> TranslateAsync(
        string apiKey,
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentNullException.ThrowIfNull(request);

        var protectedText = TranslationTextProtector.Protect(request.Text);
        if (!protectedText.HasTranslatableContent)
        {
            return new TranslationResult(request.Text, "UND", "DeepL");
        }
        var requestPayload = new DeepLRequest(
            [protectedText.Payload],
            request.TargetLanguage,
            "prefer_quality_optimized",
            request.Context,
            PreserveFormatting: true,
            TagHandling: protectedText.UsesXmlTagHandling ? "xml" : null,
            TagHandlingVersion: protectedText.UsesXmlTagHandling ? "v2" : null,
            IgnoreTags: protectedText.UsesXmlTagHandling ? ["keep"] : null,
            NonSplittingTags: protectedText.UsesXmlTagHandling ? ["keep"] : null,
            SplitSentences: protectedText.UsesXmlTagHandling ? "nonewlines" : null);
        var requestBytes = JsonSerializer.SerializeToUtf8Bytes(
            requestPayload,
            RequestJsonOptions);
        if (requestBytes.Length > MaximumRequestBytes)
        {
            throw new TranslationException(
                TranslationFailureCode.SelectionTooLarge,
                "Protected translation input exceeds the provider request limit.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            apiKey.EndsWith(":fx", StringComparison.Ordinal)
                ? FreeEndpoint
                : ProEndpoint)
        {
            Content = new ByteArrayContent(requestBytes),
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8",
        };
        message.Headers.Authorization = new AuthenticationHeaderValue(
            "DeepL-Auth-Key",
            apiKey);
        message.Headers.UserAgent.ParseAdd("Keyina/0.1");

        try
        {
            using var response = await httpClient.SendAsync(
                    message,
                    HttpCompletionOption.ResponseHeadersRead,
                    timeout.Token)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw CreateHttpFailure(response.StatusCode);
            }

            var bytes = await ReadBoundedAsync(response.Content, timeout.Token)
                .ConfigureAwait(false);
            DeepLResponse? payload;
            try
            {
                payload = JsonSerializer.Deserialize<DeepLResponse>(bytes, ResponseJsonOptions);
            }
            catch (JsonException exception)
            {
                throw new TranslationException(
                    TranslationFailureCode.InvalidResponse,
                    "The translation provider returned an invalid response.",
                    exception);
            }

            var translation = payload?.Translations is { Length: 1 }
                ? payload.Translations[0]
                : null;
            if (translation is null ||
                string.IsNullOrWhiteSpace(translation.Text) ||
                string.IsNullOrWhiteSpace(translation.DetectedSourceLanguage))
            {
                throw new TranslationException(
                    TranslationFailureCode.InvalidResponse,
                    "The translation provider returned an incomplete response.");
            }

            var restoredText = protectedText.Restore(translation.Text);
            if (string.IsNullOrWhiteSpace(restoredText))
            {
                throw new TranslationException(
                    TranslationFailureCode.InvalidResponse,
                    "The translation provider returned an empty restored response.");
            }

            return new TranslationResult(
                restoredText,
                translation.DetectedSourceLanguage,
                "DeepL");
        }
        catch (TranslationException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranslationException(
                TranslationFailureCode.Unavailable,
                "The translation provider timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw new TranslationException(
                TranslationFailureCode.Unavailable,
                "The translation provider is unavailable.",
                exception);
        }
    }

    private static TranslationException CreateHttpFailure(HttpStatusCode statusCode) =>
        (int)statusCode switch
        {
            403 => new TranslationException(
                TranslationFailureCode.AuthenticationFailed,
                "The DeepL API key was rejected."),
            429 => new TranslationException(
                TranslationFailureCode.RateLimited,
                "The translation provider rate limit was reached."),
            456 => new TranslationException(
                TranslationFailureCode.QuotaExceeded,
                "The DeepL translation quota was exhausted."),
            _ => new TranslationException(
                TranslationFailureCode.Unavailable,
                "The translation provider is unavailable."),
        };

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaximumResponseBytes)
        {
            throw new TranslationException(
                TranslationFailureCode.InvalidResponse,
                "The translation provider response was too large.");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            if (buffer.Length + read > MaximumResponseBytes)
            {
                throw new TranslationException(
                    TranslationFailureCode.InvalidResponse,
                    "The translation provider response was too large.");
            }
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private sealed record DeepLRequest(
        [property: JsonPropertyName("text")] string[] Text,
        [property: JsonPropertyName("target_lang")] string TargetLanguage,
        [property: JsonPropertyName("model_type")] string ModelType,
        [property: JsonPropertyName("context")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? Context,
        [property: JsonPropertyName("preserve_formatting")]
        bool PreserveFormatting,
        [property: JsonPropertyName("tag_handling")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? TagHandling,
        [property: JsonPropertyName("tag_handling_version")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? TagHandlingVersion,
        [property: JsonPropertyName("ignore_tags")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string[]? IgnoreTags,
        [property: JsonPropertyName("non_splitting_tags")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string[]? NonSplittingTags,
        [property: JsonPropertyName("split_sentences")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? SplitSentences);

    private sealed record DeepLResponse(
        [property: JsonPropertyName("translations")] DeepLTranslation[] Translations);

    private sealed record DeepLTranslation(
        [property: JsonPropertyName("detected_source_language")]
        string DetectedSourceLanguage,
        [property: JsonPropertyName("text")] string Text);
}
