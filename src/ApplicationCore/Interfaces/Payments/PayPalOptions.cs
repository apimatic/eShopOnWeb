using System;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

/// <summary>
/// Settings bound from the <c>PayPal:</c> configuration section. Values are supplied via
/// environment variables / user-secrets and are never committed to the repository.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary><c>sandbox</c> (default) or <c>live</c>.</summary>
    public string? Environment { get; set; }

    /// <summary>ISO-4217 currency code for all amounts, e.g. <c>USD</c>.</summary>
    public string? Currency { get; set; }

    /// <summary>Optional base-URL override; when set it is used verbatim for every PayPal call.</summary>
    public string? BaseUrl { get; set; }

    public string CurrencyCode =>
        string.IsNullOrWhiteSpace(Currency) ? "USD" : Currency!.Trim().ToUpperInvariant();

    public bool IsLive =>
        string.Equals(Environment?.Trim(), "live", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Environment?.Trim(), "production", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The API base address. When <see cref="BaseUrl"/> is set it wins for every call (including
    /// the token request); otherwise it is derived from the environment.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.Trim().TrimEnd('/');
        }

        return IsLive ? "https://api-m.paypal.com" : "https://api-m.sandbox.paypal.com";
    }
}
