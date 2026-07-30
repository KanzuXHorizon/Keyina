using System.Net;
using System.Net.Http.Headers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Keyina.Host.Core.Translation;
using Keyina.Host.Windows.Networking;

namespace Keyina.Host.Translation;

public sealed class LibreTranslateProvider : ITranslationProvider
{
    private const int MaximumRequestBytes = 128 * 1024;
    private const int MaximumResponseBytes = 256 * 1024;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(8);
    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    private readonly HttpClient httpClient;
    private readonly SafeEndpointValidator endpointValidator;
    private readonly Func<TranslationProviderPreferences> preferencesProvider;

    public LibreTranslateProvider(
        HttpClient httpClient,
        SafeEndpointValidator endpointValidator,
        Func<TranslationProviderPreferences> preferencesProvider)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.endpointValidator = endpointValidator ??
            throw new ArgumentNullException(nameof(endpointValidator));
        this.preferencesProvider = preferencesProvider ??
            throw new ArgumentNullException(nameof(preferencesProvider));
    }

    public async Task<TranslationResult> TranslateAsync(
        string apiKey,
        TranslationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var preferences = preferencesProvider().Normalize();
        if (!preferences.LibreTranslateEnabled)
        {
            throw new TranslationException(
                TranslationFailureCode.Unavailable,
                "LibreTranslate fallback is disabled.");
        }

        Uri endpoint;
        try
        {
            endpoint = await endpointValidator.ValidateTranslateEndpointAsync(
                    preferences.LibreTranslateEndpoint,
                    preferences.AllowLocalEndpoint,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (ArgumentException exception)
        {
            throw new TranslationException(
                TranslationFailureCode.Unavailable,
                "LibreTranslate endpoint is unsafe or unavailable.",
                exception);
        }

        var protectedText = TranslationTextProtector.Protect(request.Text);
        if (!protectedText.HasTranslatableContent)
        {
            return new TranslationResult(request.Text, "UND", "LibreTranslate");
        }

        var payload = new LibreTranslateRequest(
            protectedText.Payload,
            Source: "auto",
            Target: MapTargetLanguage(request.TargetLanguage),
            Format: protectedText.UsesXmlTagHandling ? "html" : "text",
            ApiKey: string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim());
        var requestBytes = JsonSerializer.SerializeToUtf8Bytes(
            payload,
            RequestJsonOptions);
        if (requestBytes.Length > MaximumRequestBytes)
        {
            throw new TranslationException(
                TranslationFailureCode.SelectionTooLarge,
                "Protected translation input exceeds the fallback request limit.");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);
        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new ByteArrayContent(requestBytes),
        };
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8",
        };
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
            LibreTranslateResponse? responsePayload;
            try
            {
                responsePayload = JsonSerializer.Deserialize<LibreTranslateResponse>(
                    bytes,
                    ResponseJsonOptions);
            }
            catch (JsonException exception)
            {
                throw new TranslationException(
                    TranslationFailureCode.InvalidResponse,
                    "LibreTranslate returned invalid JSON.",
                    exception);
            }

            if (responsePayload is null ||
                string.IsNullOrWhiteSpace(responsePayload.TranslatedText))
            {
                throw new TranslationException(
                    TranslationFailureCode.InvalidResponse,
                    "LibreTranslate returned an incomplete response.");
            }
            var restored = protectedText.Restore(responsePayload.TranslatedText);
            if (string.IsNullOrWhiteSpace(restored))
            {
                throw new TranslationException(
                    TranslationFailureCode.InvalidResponse,
                    "LibreTranslate returned an empty restored response.");
            }

            return new TranslationResult(
                restored,
                string.IsNullOrWhiteSpace(responsePayload.DetectedLanguage?.Language)
                    ? "UND"
                    : responsePayload.DetectedLanguage.Language.ToUpperInvariant(),
                "LibreTranslate");
        }
        catch (TranslationException)
        {
            throw;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranslationException(
                TranslationFailureCode.Unavailable,
                "LibreTranslate timed out.");
        }
        catch (HttpRequestException exception)
        {
            throw new TranslationException(
                TranslationFailureCode.Unavailable,
                "LibreTranslate is unavailable.",
                exception);
        }
    }

    private static string MapTargetLanguage(string targetLanguage) =>
        targetLanguage.ToUpperInvariant() switch
        {
            "EN-US" or "EN-GB" => "en",
            "PT-BR" or "PT-PT" => "pt",
            "ZH-HANS" or "ZH-HANT" => "zh",
            var value => value.ToLowerInvariant(),
        };

    private static TranslationException CreateHttpFailure(HttpStatusCode statusCode) =>
        (int)statusCode switch
        {
            401 or 403 => new TranslationException(
                TranslationFailureCode.AuthenticationFailed,
                "LibreTranslate rejected the API key."),
            429 => new TranslationException(
                TranslationFailureCode.RateLimited,
                "LibreTranslate rate limit was reached."),
            400 => new TranslationException(
                TranslationFailureCode.InvalidResponse,
                "LibreTranslate rejected the translation request."),
            _ => new TranslationException(
                TranslationFailureCode.Unavailable,
                "LibreTranslate is unavailable."),
        };

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaximumResponseBytes)
        {
            throw new TranslationException(
                TranslationFailureCode.InvalidResponse,
                "LibreTranslate response was too large.");
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
                    "LibreTranslate response was too large.");
            }
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private sealed record LibreTranslateRequest(
        [property: JsonPropertyName("q")] string Query,
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("target")] string Target,
        [property: JsonPropertyName("format")] string Format,
        [property: JsonPropertyName("api_key")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? ApiKey);

    private sealed record LibreTranslateResponse(
        [property: JsonPropertyName("translatedText")] string TranslatedText,
        [property: JsonPropertyName("detectedLanguage")]
        LibreDetectedLanguage? DetectedLanguage);

    private sealed record LibreDetectedLanguage(
        [property: JsonPropertyName("language")] string Language,
        [property: JsonPropertyName("confidence")] double Confidence);
}
