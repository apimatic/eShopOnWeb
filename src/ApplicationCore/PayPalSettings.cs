using System;

namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the "PayPal:" configuration section. None of
/// these values are hard-coded anywhere; credentials are loaded from the environment into
/// .NET user-secrets and never committed to the repository.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public const string SandboxBaseUrl = "https://api-m.sandbox.paypal.com";
    public const string LiveBaseUrl = "https://api-m.paypal.com";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>"sandbox" (default) or "live"/"production".</summary>
    public string? Environment { get; set; }

    /// <summary>ISO 4217 currency code used for all amounts, e.g. "USD".</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Optional API base-address override. When set, it is used verbatim for every PayPal call
    /// (including the OAuth token request) instead of one derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Resolves the effective API base URL: the explicit override if present, else the
    /// sandbox or live host chosen by <see cref="Environment"/>.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            return BaseUrl.TrimEnd('/');

        var env = Environment?.Trim().ToLowerInvariant();
        return env is "live" or "production" ? LiveBaseUrl : SandboxBaseUrl;
    }

    public string ResolvedCurrency => string.IsNullOrWhiteSpace(Currency) ? "USD" : Currency.Trim().ToUpperInvariant();
}
