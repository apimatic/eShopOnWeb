using System;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal configuration, bound from the "PayPal" section
/// (PayPal:ClientId, PayPal:ClientSecret, PayPal:Environment, PayPal:Currency, PayPal:BaseUrl).
/// Values come from user-secrets / environment variables; none are hard-coded.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    /// <summary>"sandbox" or "live".</summary>
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";
    /// <summary>Optional override used verbatim as the API base address for every PayPal call,
    /// including the token request, instead of deriving one from Environment.</summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }
        return string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase)
            ? "https://api-m.paypal.com"
            : "https://api-m.sandbox.paypal.com";
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal credentials are not configured. Set PayPal:ClientId and PayPal:ClientSecret " +
                "(e.g. from the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET environment variables into user-secrets).");
        }
    }
}
