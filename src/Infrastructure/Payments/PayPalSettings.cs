using System;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal integration settings, bound from the "PayPal" configuration section. Values are supplied via
/// configuration/user-secrets/environment — none are hard-coded — so the same build can target a different
/// PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Environment { get; set; }
    public string? Currency { get; set; }

    /// <summary>Optional base-URL override. When set it is used verbatim for every PayPal call, including the token request.</summary>
    public string? BaseUrl { get; set; }

    public string ResolvedCurrency => string.IsNullOrWhiteSpace(Currency) ? "USD" : Currency!.Trim().ToUpperInvariant();

    /// <summary>
    /// The API base address. Uses <see cref="BaseUrl"/> verbatim when set; otherwise derives sandbox/live from
    /// <see cref="Environment"/> (defaulting to sandbox for any value that is not explicitly live/production).
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            return BaseUrl!.TrimEnd('/');

        var env = Environment?.Trim().ToLowerInvariant();
        var isLive = env is "live" or "production" or "prod";
        return isLive ? "https://api-m.paypal.com" : "https://api-m.sandbox.paypal.com";
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId))
            throw new InvalidOperationException("PayPal:ClientId is not configured.");
        if (string.IsNullOrWhiteSpace(ClientSecret))
            throw new InvalidOperationException("PayPal:ClientSecret is not configured.");
        if (string.IsNullOrWhiteSpace(Currency))
            throw new InvalidOperationException("PayPal:Currency is not configured.");
    }
}
