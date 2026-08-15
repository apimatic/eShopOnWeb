using System;

namespace Microsoft.eShopWeb.ApplicationCore.Models.PayPal;

/// <summary>
/// Settings bound from the "PayPal:" configuration section. Values are supplied via configuration
/// (user-secrets / environment) and never hard-coded, so the same build runs against any account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live"/"production".</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency all payments are denominated in.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base address for every PayPal
    /// call (including the token request) instead of deriving one from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Resolves the API base address, honouring <see cref="BaseUrl"/> when present.</summary>
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
