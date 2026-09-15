using System;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Settings for the PayPal integration, bound from the "PayPal:" configuration section. Values are never
/// hard-coded — they come from user-secrets / environment (PAYPAL_*), so the same build can run against a
/// different PayPal account.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    public const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    public const string LiveBaseUrl = "https://api-m.paypal.com";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>"sandbox" (default) or "live"/"production".</summary>
    public string? Environment { get; set; }

    /// <summary>ISO-4217 currency the integration transacts in.</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Optional explicit base URL. When set, it is used verbatim for every PayPal call — including the token
    /// request — instead of deriving one from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsLive =>
        string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Environment, "production", StringComparison.OrdinalIgnoreCase);

    /// <summary>The base address for every PayPal API call, honoring the explicit override when present.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }
        return IsLive ? LiveBaseUrl : SandboxBaseUrl;
    }

    public string ResolveCurrency() => string.IsNullOrWhiteSpace(Currency) ? "USD" : Currency!.Trim().ToUpperInvariant();

    /// <summary>Throws if the credentials required to talk to PayPal are missing.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret " +
                "(from PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET) via user-secrets or environment variables.");
        }
    }
}
