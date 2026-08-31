using System;

namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Settings for the PayPal integration. Bound from the "PayPal" configuration section.
/// Values are supplied through environment variables / user-secrets, never from files in the repo.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";

    public const string SANDBOX_BASE_URL = "https://api-m.sandbox.paypal.com";
    public const string LIVE_BASE_URL = "https://api-m.paypal.com";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional override. When set, it is used verbatim as the base address for every
    /// PayPal call (including the token request) instead of deriving one from Environment.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolvedBaseUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(BaseUrl))
            {
                return BaseUrl.TrimEnd('/');
            }

            return string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase)
                ? LIVE_BASE_URL
                : SANDBOX_BASE_URL;
        }
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal credentials are not configured. Set the PayPal:ClientId and PayPal:ClientSecret " +
                "configuration keys (from the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET environment variables, " +
                "e.g. via .NET user-secrets).");
        }

        if (string.IsNullOrWhiteSpace(Currency))
        {
            throw new InvalidOperationException("PayPal:Currency is not configured (from the PAYPAL_CURRENCY environment variable).");
        }
    }
}
