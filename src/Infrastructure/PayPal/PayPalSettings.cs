using System;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the <c>PayPal:</c> section. All values come
/// from configuration (env vars / user-secrets) &mdash; none are hard-coded.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live"/"production". Determines the API base URL when <see cref="BaseUrl"/> is unset.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency for amounts, e.g. "USD".</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim for every PayPal call
    /// (including the OAuth token request) instead of one derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Resolves the base address: the explicit override, else the environment default.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            return BaseUrl.TrimEnd('/');

        var isLive = Environment.Trim().ToLowerInvariant() is "live" or "production";
        return isLive ? "https://api-m.paypal.com" : "https://api-m.sandbox.paypal.com";
    }
}
