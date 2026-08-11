using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Bound from the <c>PayPal:</c> configuration section. Values are never hard-coded; they come
/// from configuration (user-secrets fed from environment variables) so the same build can run
/// against a different PayPal account.
/// </summary>
public class PayPalSettings : IPaymentConfiguration
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live"/"production". Drives the derived base URL when no override is set.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency for amounts (e.g. USD).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base address for EVERY PayPal
    /// call — including the OAuth token request — instead of deriving one from the environment.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Resolves the base address per the override-or-environment rule.</summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return Environment?.Trim().ToLowerInvariant() switch
        {
            "live" or "production" => "https://api-m.paypal.com",
            _ => "https://api-m.sandbox.paypal.com"
        };
    }
}
