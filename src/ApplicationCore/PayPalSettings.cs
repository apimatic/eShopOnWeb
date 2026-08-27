using System;

namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Settings bound from the "PayPal" configuration section. Values are supplied via
/// user-secrets or environment variables; none are hard-coded.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional override. When set, it is used verbatim as the base address for every
    /// PayPal call (including the OAuth token request) instead of deriving one from
    /// <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    // Base URLs come from the PayPal OpenAPI specs in api-specs/ (servers section).
    public string ApiBaseUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(BaseUrl))
            {
                return BaseUrl!.TrimEnd('/');
            }

            return Environment.Equals("live", StringComparison.OrdinalIgnoreCase)
                || Environment.Equals("production", StringComparison.OrdinalIgnoreCase)
                ? "https://api-m.paypal.com"
                : "https://api-m.sandbox.paypal.com";
        }
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId))
            throw new InvalidOperationException("PayPal:ClientId is not configured. Set it via user-secrets or the PAYPAL_CLIENT_ID environment variable.");
        if (string.IsNullOrWhiteSpace(ClientSecret))
            throw new InvalidOperationException("PayPal:ClientSecret is not configured. Set it via user-secrets or the PAYPAL_CLIENT_SECRET environment variable.");
        if (string.IsNullOrWhiteSpace(Currency))
            throw new InvalidOperationException("PayPal:Currency is not configured. Set it via user-secrets or the PAYPAL_CURRENCY environment variable.");
    }
}
