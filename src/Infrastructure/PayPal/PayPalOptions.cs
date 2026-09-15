using System;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Settings bound from the <c>PayPal:</c> configuration section. Values are never hard-coded — they are supplied
/// via configuration/user-secrets so the same build can run against a different PayPal account.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live"/"production". Determines the API host when <see cref="BaseUrl"/> is not set.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency for all amounts.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Optional explicit base URL override. When set, it is used verbatim for every PayPal call, including the token request.</summary>
    public string? BaseUrl { get; set; }

    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    /// <summary>
    /// The base address to use for all PayPal calls. Uses <see cref="BaseUrl"/> verbatim when supplied; otherwise
    /// derives the host from <see cref="Environment"/>.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        return Environment?.Trim().ToLowerInvariant() switch
        {
            "live" or "production" => LiveBaseUrl,
            _ => SandboxBaseUrl
        };
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId))
        {
            throw new InvalidOperationException("PayPal:ClientId is not configured. Set it via user-secrets (from PAYPAL_CLIENT_ID).");
        }
        if (string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException("PayPal:ClientSecret is not configured. Set it via user-secrets (from PAYPAL_CLIENT_SECRET).");
        }
        if (string.IsNullOrWhiteSpace(Currency))
        {
            throw new InvalidOperationException("PayPal:Currency is not configured (from PAYPAL_CURRENCY).");
        }
    }
}
