using System;

namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Bound from the "PayPal" configuration section. Values are supplied through
/// user-secrets / environment variables - never hard-coded.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base address
    /// for every PayPal call, including the OAuth token request.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Environment, "production", StringComparison.OrdinalIgnoreCase)
                ? "https://api-m.paypal.com"
                : "https://api-m.sandbox.paypal.com";
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret " +
                "(from the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET environment variables) via user-secrets or environment configuration.");
        }

        if (string.IsNullOrWhiteSpace(Currency))
        {
            throw new InvalidOperationException(
                "PayPal:Currency is not configured. Set it from the PAYPAL_CURRENCY environment variable.");
        }
    }
}
