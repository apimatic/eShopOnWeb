using System;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Settings bound from the "PayPal" configuration section. Values arrive via environment
/// variables (PAYPAL_CLIENT_ID, PAYPAL_CLIENT_SECRET, PAYPAL_ENVIRONMENT, PAYPAL_CURRENCY)
/// or user-secrets; none are hard-coded.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    /// <summary>"sandbox" or "live". Determines the PayPal API base address.</summary>
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base address for every
    /// PayPal call, including the OAuth token request.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        // Server URLs come from the PayPal OpenAPI specifications in api-specs/paypal.
        return Environment.Equals("live", StringComparison.OrdinalIgnoreCase)
            || Environment.Equals("production", StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }
}
