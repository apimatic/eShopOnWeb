using System;

namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Bound from the "PayPal:" configuration section. Values are never hard-coded — they
/// come from configuration / user-secrets so the same build runs against any account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>"sandbox" or "live"/"production".</summary>
    public string? Environment { get; set; }

    /// <summary>ISO-4217 currency code used for all amounts, e.g. "USD".</summary>
    public string? Currency { get; set; }

    /// <summary>Optional override. When set, it is used verbatim as the API base
    /// address for every PayPal call (including the token request), instead of a URL
    /// derived from <see cref="Environment"/>.</summary>
    public string? BaseUrl { get; set; }

    private const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    private const string LiveBaseUrl = "https://api-m.paypal.com";

    /// <summary>The effective API base address: the explicit override when present,
    /// otherwise derived from the environment.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        var env = (Environment ?? "sandbox").Trim().ToLowerInvariant();
        return env is "live" or "production" ? LiveBaseUrl : SandboxBaseUrl;
    }

    public string EffectiveCurrency => string.IsNullOrWhiteSpace(Currency) ? "USD" : Currency.Trim().ToUpperInvariant();
}
