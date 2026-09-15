using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// PayPal configuration, bound from the <c>PayPal:</c> section. Values are supplied via
/// user-secrets / environment variables and are never hard-coded.
/// </summary>
public class PayPalSettings : IPaymentSettings
{
    public const string SectionName = "PayPal";

    /// <summary>REST client id (from <c>PAYPAL_CLIENT_ID</c>).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>REST client secret (from <c>PAYPAL_CLIENT_SECRET</c>).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live"/"production" (from <c>PAYPAL_ENVIRONMENT</c>).</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency (from <c>PAYPAL_CURRENCY</c>).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim for every PayPal call —
    /// including the token request — instead of deriving one from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The resolved API base address, honouring <see cref="BaseUrl"/> when present.</summary>
    public string ResolvedBaseUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(BaseUrl))
                return BaseUrl!.TrimEnd('/');

            var isLive = Environment?.Trim().ToLowerInvariant() is "live" or "production";
            return isLive ? "https://api-m.paypal.com" : "https://api-m.sandbox.paypal.com";
        }
    }
}
