using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the <c>PayPal</c> configuration section.
/// Values are supplied via environment variables / user-secrets and must never be hard-coded.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" (default) or "live"/"production".</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>Three-letter ISO-4217 currency code that every amount is denominated in.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim for every PayPal call
    /// (including the OAuth token request) instead of deriving one from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Resolves the API base address, honoring the <see cref="BaseUrl"/> override.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return (Environment ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "live" or "production" => "https://api-m.paypal.com",
            _ => "https://api-m.sandbox.paypal.com",
        };
    }

    /// <summary>Number of decimal places PayPal expects for the configured currency.</summary>
    public int CurrencyDecimals => CurrencyDecimalsFor(Currency);

    // A pragmatic subset of ISO-4217 zero-decimal currencies. Everything else is treated as 2dp.
    private static int CurrencyDecimalsFor(string currency) => (currency ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "JPY" or "KRW" or "VND" or "CLP" or "ISK" or "HUF" or "TWD" or "UGX" or "XAF" or "XOF" or "RWF" or "DJF" or "GNF" or "KMF" or "PYG" or "VUV" or "XPF" => 0,
        _ => 2,
    };
}
