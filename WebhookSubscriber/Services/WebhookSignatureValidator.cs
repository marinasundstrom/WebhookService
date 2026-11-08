
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace WebhookSubscriber.Services;

public sealed class WebhookSignatureValidator
{
    private readonly bool _signatureValidationDisabled;
    private readonly byte[] _secret;

    public WebhookSignatureValidator(IConfiguration configuration)
    {
        var secret = configuration["Webhooks:Secret"]
            ?? throw new InvalidOperationException(
                "Webhook secret is not configured.");

        _signatureValidationDisabled = configuration.GetValue<bool>("Webhooks:DisableSignatureValidation");

        _secret = Encoding.UTF8.GetBytes(secret);
    }

    public bool Validate(
        ReadOnlySpan<byte> body,
        string? timestamp,
        string? signature)
    {
        if(_signatureValidationDisabled)
            return true;
        
        if (string.IsNullOrWhiteSpace(timestamp) ||
            string.IsNullOrWhiteSpace(signature))
            return false;

        if (!long.TryParse(
            timestamp,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out var unixTimestamp))
            return false;

        DateTimeOffset sentAt;

        try
        {
            sentAt = DateTimeOffset.FromUnixTimeSeconds(
                unixTimestamp);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        // Reject requests outside the five-minute window.
        if (Math.Abs(
            (DateTimeOffset.UtcNow - sentAt).TotalMinutes) > 5)
            return false;

        const string prefix = "sha256=";

        if (!signature.StartsWith(
            prefix, StringComparison.Ordinal))
            return false;

        byte[] providedSignature;

        try
        {
            providedSignature = Convert.FromHexString(
                signature[prefix.Length..]);
        }
        catch (FormatException)
        {
            return false;
        }

        var timestampBytes = Encoding.UTF8.GetBytes(
            timestamp + ".");

        var signedContent = new byte[
            timestampBytes.Length + body.Length];

        timestampBytes.CopyTo(signedContent, 0);
        body.CopyTo(signedContent.AsSpan(timestampBytes.Length));

        var expectedSignature = HMACSHA256.HashData(
            _secret, signedContent);

        return CryptographicOperations.FixedTimeEquals(
            expectedSignature, providedSignature);
    }
}
