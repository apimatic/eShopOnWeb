using System;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Bound from the "PayPal" configuration section (keys: ClientId, ClientSecret,
/// Environment, Currency, BaseUrl). Values are supplied through user-secrets or
/// environment variables — never from files in this repository.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live". Determines the API base address unless BaseUrl is set.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency used for every payment operation.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base address
    /// for every PayPal call, including the OAuth token request.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string GetBaseUrl()
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
                "(e.g. from the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET environment variables via user-secrets).");
        }
    }
}
