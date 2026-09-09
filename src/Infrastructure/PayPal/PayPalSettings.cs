using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Settings bound from the <c>PayPal:</c> configuration section. Values are supplied via
/// user-secrets / environment (never hard-coded), so the same build can run against a different
/// PayPal account.
/// </summary>
public class PayPalSettings : IPayPalConfiguration
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" (default) or "live"/"production".</summary>
    public string Environment { get; set; } = "sandbox";

    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional API base-address override. When set it is used verbatim for EVERY PayPal call
    /// (including the token request); otherwise the base is derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            return BaseUrl.TrimEnd('/') + "/";

        var isLive = Environment?.Trim().ToLowerInvariant() is "live" or "production";
        return isLive ? "https://api-m.paypal.com/" : "https://api-m.sandbox.paypal.com/";
    }
}
