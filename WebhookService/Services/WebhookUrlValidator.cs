
using System.Net;
using System.Net.Sockets;

namespace WebhookService.Services;

public static class WebhookUrlValidator
{
    public static async Task ValidateAsync(
        Uri url,
        CancellationToken ct = default)
    {
        if (url.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException(
                "Webhook endpoints must use HTTPS.");

        if (!url.IsDefaultPort || !string.IsNullOrEmpty(url.UserInfo))
            throw new ArgumentException(
                "Custom ports and credentials are not allowed.");

        var addresses = await Dns.GetHostAddressesAsync(
            url.DnsSafeHost, ct);

        if (addresses.Length == 0 ||
            addresses.Any(ip => !IsPublic(ip)))
        {
            throw new ArgumentException(
                "Webhook endpoint must resolve to public IPs.");
        }
    }

    private static bool IsPublic(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
            return false;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();

            return b[0] != 0 &&
                   b[0] != 10 &&
                   b[0] != 127 &&
                   b[0] < 224 &&
                   !(b[0] == 169 && b[1] == 254) &&
                   !(b[0] == 172 && b[1] is >= 16 and <= 31) &&
                   !(b[0] == 192 && b[1] == 168) &&
                   !(b[0] == 100 && b[1] is >= 64 and <= 127) &&
                   !(b[0] == 192 && b[1] == 0 && b[2] == 0) &&
                   !(b[0] == 192 && b[1] == 0 && b[2] == 2) &&
                   !(b[0] == 198 && b[1] == 51 && b[2] == 100) &&
                   !(b[0] == 203 && b[1] == 0 && b[2] == 113);
        }

        // Conservative default: reject IPv6 until a complete
        // public-address policy is implemented.
        return false;
    }
}
