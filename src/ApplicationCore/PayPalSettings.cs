using System;

namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the <c>PayPal:</c> configuration section.
/// None of these values are hard-coded — the same build must run against a different PayPal
/// account by changing configuration alone.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live"/"production". Used to derive the base URL when it is not overridden.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>The three-letter ISO-4217 currency code used for all amounts (e.g. USD).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional base-URL override. When set, it is used verbatim as the API base address for
    /// every PayPal call — including the token request — instead of deriving one from
    /// <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The API base address to use for every PayPal call, honoring the override.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        var isLive = Environment.Equals("live", StringComparison.OrdinalIgnoreCase)
            || Environment.Equals("production", StringComparison.OrdinalIgnoreCase);

        return isLive
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }
}
