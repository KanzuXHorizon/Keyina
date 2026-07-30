using System.Net;
using Keyina.Host.Windows.Networking;

namespace Keyina.Host.Tests;

internal static class SafeEndpointValidatorTests
{
    [KeyinaTest("safe endpoint validator accepts public HTTPS and appends translate path")]
    private static void AcceptsPublicHttps()
    {
        var validator = new SafeEndpointValidator(
            (host, _) => Task.FromResult(
                new[] { IPAddress.Parse("93.184.216.34") }));

        var endpoint = validator.ValidateTranslateEndpointAsync(
                "https://translate.example/api",
                allowLocalEndpoint: false,
                CancellationToken.None)
            .GetAwaiter().GetResult();

        AssertEx.Equal("https://translate.example/api/translate", endpoint.AbsoluteUri);
    }

    [KeyinaTest("safe endpoint validator rejects HTTP public private loopback and userinfo by default")]
    private static void RejectsUnsafePublicEndpoints()
    {
        var publicValidator = new SafeEndpointValidator(
            (_, _) => Task.FromResult(
                new[] { IPAddress.Parse("93.184.216.34") }));
        AssertThrows<ArgumentException>(() => publicValidator
            .ValidateTranslateEndpointAsync(
                "http://translate.example",
                allowLocalEndpoint: false,
                CancellationToken.None)
            .GetAwaiter().GetResult());
        AssertThrows<ArgumentException>(() => publicValidator
            .ValidateTranslateEndpointAsync(
                "https://user:pass@translate.example",
                allowLocalEndpoint: false,
                CancellationToken.None)
            .GetAwaiter().GetResult());

        foreach (var address in new[]
                 {
                     "127.0.0.1",
                     "10.0.0.4",
                     "172.16.0.4",
                     "192.168.1.4",
                     "169.254.1.4",
                     "::1",
                     "fc00::1",
                     "fe80::1",
                 })
        {
            var validator = new SafeEndpointValidator(
                (_, _) => Task.FromResult(new[] { IPAddress.Parse(address) }));
            AssertThrows<ArgumentException>(() => validator
                .ValidateTranslateEndpointAsync(
                    "https://translate.example",
                    allowLocalEndpoint: false,
                    CancellationToken.None)
                .GetAwaiter().GetResult());
        }
    }

    [KeyinaTest("safe endpoint validator permits explicit local HTTP only for local addresses")]
    private static void LocalModeIsExplicitAndBounded()
    {
        var localValidator = new SafeEndpointValidator(
            (_, _) => Task.FromResult(
                new[] { IPAddress.Loopback }));
        var endpoint = localValidator.ValidateTranslateEndpointAsync(
                "http://localhost:5000/",
                allowLocalEndpoint: true,
                CancellationToken.None)
            .GetAwaiter().GetResult();
        AssertEx.Equal("http://localhost:5000/translate", endpoint.AbsoluteUri);

        var publicValidator = new SafeEndpointValidator(
            (_, _) => Task.FromResult(
                new[] { IPAddress.Parse("93.184.216.34") }));
        AssertThrows<ArgumentException>(() => publicValidator
            .ValidateTranslateEndpointAsync(
                "http://translate.example",
                allowLocalEndpoint: true,
                CancellationToken.None)
            .GetAwaiter().GetResult());
    }

    [KeyinaTest("safe endpoint validator rejects mixed public and private DNS answers")]
    private static void RejectsMixedDnsAnswers()
    {
        var validator = new SafeEndpointValidator(
            (_, _) => Task.FromResult(
                new[]
                {
                    IPAddress.Parse("93.184.216.34"),
                    IPAddress.Parse("192.168.1.20"),
                }));

        AssertThrows<ArgumentException>(() => validator
            .ValidateTranslateEndpointAsync(
                "https://translate.example",
                allowLocalEndpoint: false,
                CancellationToken.None)
            .GetAwaiter().GetResult());
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
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
