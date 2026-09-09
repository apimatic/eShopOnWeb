using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Payments;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Settings bound from the "PayPal" configuration section. Values are supplied via user-secrets
/// / environment and never hard-coded, so the same build runs against a different PayPal account.
/// </summary>
public class PayPalOptions : IPaymentSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live"/"production".</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency amounts are charged in.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional base-URL override. When set it is used verbatim for every PayPal call (including
    /// the token request); otherwise the base URL is derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Resolves the API base address per the contract: explicit override wins, else by environment.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        return Environment?.Trim().ToLowerInvariant() switch
        {
            "live" or "production" => "https://api-m.paypal.com",
            _ => "https://api-m.sandbox.paypal.com"
        };
    }
}
