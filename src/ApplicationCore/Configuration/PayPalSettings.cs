using System;

namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the "PayPal" configuration section.
/// Values are never hard-coded; they arrive via environment variables / user-secrets.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_SECTION = "PayPal";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live"/"production".</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code used for every payment amount.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim for EVERY PayPal
    /// call (including the OAuth token request); otherwise the base address is derived from
    /// <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Resolves the API base address, honouring an explicit <see cref="BaseUrl"/> override.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        var isLive = string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(Environment, "production", StringComparison.OrdinalIgnoreCase);

        return isLive ? "https://api-m.paypal.com" : "https://api-m.sandbox.paypal.com";
    }
}
