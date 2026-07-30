using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Keyina.Host.Core.Translation;
using Keyina.Host.Translation;
using Keyina.Host.Windows.Networking;

namespace Keyina.Host.Tests;

internal static class LibreTranslateProviderTests
{
    [KeyinaTest("LibreTranslate sends the documented auto-detect JSON contract")]
    private static void SendsDocumentedContract()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            "{\"translatedText\":\"Hello\",\"detectedLanguage\":{\"confidence\":99,\"language\":\"vi\"}}"));
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        var result = provider.TranslateAsync(
                "optional-key",
                new TranslationRequest("Xin chào", "EN-US"),
                CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.Equal("Hello", result.Text);
        AssertEx.Equal("VI", result.DetectedSourceLanguage);
        AssertEx.Equal("LibreTranslate", result.Provider);
        AssertEx.Equal("https://translate.example/api/translate", handler.Request!.RequestUri!.AbsoluteUri);
        AssertEx.Equal("application/json", handler.Request.Content!.Headers.ContentType!.MediaType);
        using var document = JsonDocument.Parse(handler.Body!);
        var root = document.RootElement;
        AssertEx.Equal("Xin chào", root.GetProperty("q").GetString());
        AssertEx.Equal("auto", root.GetProperty("source").GetString());
        AssertEx.Equal("en", root.GetProperty("target").GetString());
        AssertEx.Equal("text", root.GetProperty("format").GetString());
        AssertEx.Equal("optional-key", root.GetProperty("api_key").GetString());
    }

    [KeyinaTest("LibreTranslate preserves protected technical tokens through HTML mode")]
    private static void PreservesProtectedTokens()
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            "{\"translatedText\":\"<root>Xin chào <keep id=\\\"0\\\">{name}</keep> tại <keep id=\\\"1\\\">https://example.com</keep></root>\",\"detectedLanguage\":{\"language\":\"en\",\"confidence\":90}}"));
        using var client = new HttpClient(handler);
        var provider = CreateProvider(client);

        var result = provider.TranslateAsync(
                string.Empty,
                new TranslationRequest("Hello {name} at https://example.com", "VI"),
                CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.Equal("Xin chào {name} tại https://example.com", result.Text);
        using var document = JsonDocument.Parse(handler.Body!);
        AssertEx.Equal("html", document.RootElement.GetProperty("format").GetString());
        AssertEx.False(document.RootElement.TryGetProperty("api_key", out _),
            "Optional empty API key was serialized.");
    }

    [KeyinaTest("LibreTranslate maps auth rate server and malformed responses to stable failures")]
    private static void MapsFailures()
    {
        foreach (var (statusCode, expected) in new[]
                 {
                     (HttpStatusCode.Forbidden, TranslationFailureCode.AuthenticationFailed),
                     ((HttpStatusCode)429, TranslationFailureCode.RateLimited),
                     (HttpStatusCode.InternalServerError, TranslationFailureCode.Unavailable),
                 })
        {
            using var client = new HttpClient(new RecordingHandler(_ => JsonResponse(
                statusCode,
                "{\"error\":\"failed\"}")));
            var exception = AssertThrows<TranslationException>(() =>
                CreateProvider(client).TranslateAsync(
                        "key",
                        new TranslationRequest("Hello", "VI"),
                        CancellationToken.None)
                    .GetAwaiter().GetResult());
            AssertEx.Equal(expected, exception.FailureCode);
        }

        using var invalidClient = new HttpClient(new RecordingHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            "{\"translatedText\":\"\"}")));
        var invalid = AssertThrows<TranslationException>(() =>
            CreateProvider(invalidClient).TranslateAsync(
                    string.Empty,
                    new TranslationRequest("Hello", "VI"),
                    CancellationToken.None)
                .GetAwaiter().GetResult());
        AssertEx.Equal(TranslationFailureCode.InvalidResponse, invalid.FailureCode);
    }

    private static LibreTranslateProvider CreateProvider(HttpClient client) => new(
        client,
        new SafeEndpointValidator((_, _) => Task.FromResult(
            new[] { IPAddress.Parse("93.184.216.34") })),
        () => new TranslationProviderPreferences(
            LibreTranslateEnabled: true,
            LibreTranslateEndpoint: "https://translate.example/api",
            AllowLocalEndpoint: false));

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static TException AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return responder(request);
        }
    }
}
