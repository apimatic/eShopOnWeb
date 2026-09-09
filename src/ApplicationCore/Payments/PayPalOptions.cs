using System;

namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Strongly-typed PayPal settings, bound from the <c>PayPal:</c> configuration section. Values are
/// supplied via configuration/user-secrets and are never hard-coded.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>"sandbox" or "live"/"production". Used to derive the base URL when BaseUrl is unset.</summary>
    public string? Environment { get; set; }

    /// <summary>Three-letter currency for all amounts.</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim for every PayPal call
    /// (including the token request); otherwise the base URL is derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolvedCurrency =>
        string.IsNullOrWhiteSpace(Currency) ? "USD" : Currency.Trim().ToUpperInvariant();

    /// <summary>
    /// Resolves the API base address: the explicit <see cref="BaseUrl"/> override when present,
    /// otherwise the sandbox/live host implied by <see cref="Environment"/>.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            return BaseUrl.Trim();

        var env = Environment?.Trim().ToLowerInvariant();
        return env is "live" or "production"
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }
}
