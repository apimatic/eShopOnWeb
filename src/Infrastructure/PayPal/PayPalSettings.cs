using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal settings bound from the <c>PayPal:</c> configuration section. None of these
/// values are hard-coded; the credential values are supplied via user-secrets /
/// environment and never committed to the repository.
/// </summary>
public class PayPalSettings : IPaymentConfiguration
{
    public const string SectionName = "PayPal";

    /// <summary>PayPal REST app client id (from PAYPAL_CLIENT_ID).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>PayPal REST app secret (from PAYPAL_CLIENT_SECRET).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live"/"production" (from PAYPAL_ENVIRONMENT).</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>Three-letter ISO-4217 currency for all payments (from PAYPAL_CURRENCY).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base URL. When set, it is used verbatim for every PayPal
    /// call (including the token request) instead of one derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The effective API base URL for all calls.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            return BaseUrl!.TrimEnd('/');

        var isLive = Environment.Trim().ToLowerInvariant() is "live" or "production";
        return isLive ? "https://api-m.paypal.com" : "https://api-m.sandbox.paypal.com";
    }
}
