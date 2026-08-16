using System;

namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

/// <summary>
/// Options bound from the <c>PayPal:</c> configuration section. Values are supplied through
/// user-secrets / environment configuration and are never hard-coded, so the same build can run
/// against a different PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>"sandbox" (default) or "production"/"live".</summary>
    public string? Environment { get; set; }

    /// <summary>ISO-4217 currency code used for every amount (from <c>PayPal:Currency</c>).</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim for every PayPal call,
    /// including the OAuth token request, instead of one derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string CurrencyCode =>
        string.IsNullOrWhiteSpace(Currency) ? "USD" : Currency.Trim().ToUpperInvariant();

    public bool IsProduction =>
        string.Equals(Environment?.Trim(), "production", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Environment?.Trim(), "live", StringComparison.OrdinalIgnoreCase);

    /// <summary>Resolves the API base address: the explicit override if present, else per-environment.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.Trim().TrimEnd('/');
        }

        return IsProduction
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }
}
