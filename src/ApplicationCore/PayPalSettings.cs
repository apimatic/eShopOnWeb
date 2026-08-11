using System;

namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the <c>PayPal:</c> section. None of these values are
/// hard-coded: they are supplied via configuration/user-secrets so the same build can run against a
/// different PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    /// <summary>From <c>PayPal:ClientId</c> (env <c>PAYPAL_CLIENT_ID</c>).</summary>
    public string? ClientId { get; set; }

    /// <summary>From <c>PayPal:ClientSecret</c> (env <c>PAYPAL_CLIENT_SECRET</c>).</summary>
    public string? ClientSecret { get; set; }

    /// <summary>From <c>PayPal:Environment</c> (env <c>PAYPAL_ENVIRONMENT</c>): "sandbox" or "live".</summary>
    public string? Environment { get; set; }

    /// <summary>From <c>PayPal:Currency</c> (env <c>PAYPAL_CURRENCY</c>): the ISO-4217 currency to charge in.</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// From <c>PayPal:BaseUrl</c>. Optional override. When set, it is used verbatim as the API base address
    /// for every PayPal call — including the token request — instead of one derived from the environment.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string CurrencyOrDefault() => string.IsNullOrWhiteSpace(Currency) ? "USD" : Currency!.Trim().ToUpperInvariant();

    /// <summary>
    /// The API base address to use for every call. Honors <see cref="BaseUrl"/> verbatim when present;
    /// otherwise derives it from <see cref="Environment"/>.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        var isLive = string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(Environment, "production", StringComparison.OrdinalIgnoreCase);
        return isLive ? "https://api-m.paypal.com" : "https://api-m.sandbox.paypal.com";
    }
}
