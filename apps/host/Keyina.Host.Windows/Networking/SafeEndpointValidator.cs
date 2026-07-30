using System.Net;
using System.Net.Sockets;

namespace Keyina.Host.Windows.Networking;

public sealed class SafeEndpointValidator
{
    private readonly Func<string, CancellationToken, Task<IPAddress[]>> resolver;

    public SafeEndpointValidator(
        Func<string, CancellationToken, Task<IPAddress[]>>? resolver = null)
    {
        this.resolver = resolver ?? ResolveHostAddressesAsync;
    }

    public async Task<Uri> ValidateTranslateEndpointAsync(
        string endpoint,
        bool allowLocalEndpoint,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        if (!Uri.TryCreate(endpoint.Trim(), UriKind.Absolute, out var uri))
        {
            throw new ArgumentException(
                "Translation endpoint must be an absolute URL.",
                nameof(endpoint));
        }
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            throw new ArgumentException(
                "Translation endpoint must use HTTP or HTTPS.",
                nameof(endpoint));
        }
        if (!string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new ArgumentException(
                "Translation endpoint must not contain user information or fragments.",
                nameof(endpoint));
        }
        if (uri.Query.Length != 0)
        {
            throw new ArgumentException(
                "Translation endpoint must not contain a query string.",
                nameof(endpoint));
        }
        if (uri.HostNameType == UriHostNameType.Unknown ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ArgumentException(
                "Translation endpoint host is invalid.",
                nameof(endpoint));
        }

        var addresses = await resolver(uri.DnsSafeHost, cancellationToken)
            .ConfigureAwait(false);
        if (addresses.Length == 0)
        {
            throw new ArgumentException(
                "Translation endpoint host did not resolve to an address.",
                nameof(endpoint));
        }

        var anyLocal = addresses.Any(IsLocalAddress);
        var allLocal = addresses.All(IsLocalAddress);
        if (!allowLocalEndpoint)
        {
            if (uri.Scheme != Uri.UriSchemeHttps)
            {
                throw new ArgumentException(
                    "Public translation endpoints require HTTPS.",
                    nameof(endpoint));
            }
            if (anyLocal)
            {
                throw new ArgumentException(
                    "Private, loopback, and link-local translation endpoints are blocked.",
                    nameof(endpoint));
            }
        }
        else if (uri.Scheme == Uri.UriSchemeHttp && !allLocal)
        {
            throw new ArgumentException(
                "HTTP is allowed only for explicitly enabled local translation endpoints.",
                nameof(endpoint));
        }

        var builder = new UriBuilder(uri)
        {
            Path = BuildTranslatePath(uri.AbsolutePath),
            Query = string.Empty,
            Fragment = string.Empty,
        };
        return builder.Uri;
    }

    private static string BuildTranslatePath(string path)
    {
        var normalized = string.IsNullOrWhiteSpace(path) || path == "/"
            ? string.Empty
            : path.TrimEnd('/');
        return normalized.EndsWith("/translate", StringComparison.OrdinalIgnoreCase)
            ? normalized
            : normalized + "/translate";
    }

    private static async Task<IPAddress[]> ResolveHostAddressesAsync(
        string host,
        CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literal))
        {
            return [literal];
        }
        try
        {
            return await Dns.GetHostAddressesAsync(host, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (SocketException exception)
        {
            throw new ArgumentException(
                "Translation endpoint host could not be resolved.",
                nameof(host),
                exception);
        }
    }

    private static bool IsLocalAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.IsIPv6LinkLocal ||
            address.IsIPv6SiteLocal || address.IsIPv6Multicast)
        {
            return true;
        }
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            return (bytes[0] & 0xFE) == 0xFC;
        }
        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return true;
        }

        var octets = address.GetAddressBytes();
        return octets[0] switch
        {
            0 => true,
            10 => true,
            100 when octets[1] is >= 64 and <= 127 => true,
            127 => true,
            169 when octets[1] == 254 => true,
            172 when octets[1] is >= 16 and <= 31 => true,
            192 when octets[1] == 0 => true,
            192 when octets[1] == 168 => true,
            198 when octets[1] is 18 or 19 => true,
            >= 224 => true,
            _ => false,
        };
    }
}
