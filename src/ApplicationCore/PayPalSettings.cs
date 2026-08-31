using System;

namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Settings bound from the "PayPal" configuration section. Values are supplied
/// via environment/user-secrets (PAYPAL_CLIENT_ID, PAYPAL_CLIENT_SECRET,
/// PAYPAL_ENVIRONMENT, PAYPAL_CURRENCY); none are hard-coded.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set, it is used verbatim as the base address for
    /// every PayPal call, including the OAuth token request.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ApiBaseUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(BaseUrl))
            {
                return BaseUrl.TrimEnd('/');
            }

            return string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase)
                ? "https://api-m.paypal.com"
                : "https://api-m.sandbox.paypal.com";
        }
    }
}
