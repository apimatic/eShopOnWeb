using System;

namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Bound from the "PayPal" configuration section. Values arrive via user-secrets or
/// environment variables; none are hard-coded or stored in the repository.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";

    /// <summary>Optional override. When set, used verbatim as the API base address for
    /// every PayPal call, including the token request.</summary>
    public string? BaseUrl { get; set; }

    public string ApiBaseUrl
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(BaseUrl))
            {
                return BaseUrl!.TrimEnd('/');
            }

            return string.Equals(Environment, "live", StringComparison.OrdinalIgnoreCase)
                ? "https://api-m.paypal.com"
                : "https://api-m.sandbox.paypal.com";
        }
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ClientId) || string.IsNullOrWhiteSpace(ClientSecret))
        {
            throw new InvalidOperationException(
                "PayPal credentials are not configured. Set the PayPal:ClientId and PayPal:ClientSecret " +
                "configuration keys (e.g. via .NET user-secrets from the PAYPAL_CLIENT_ID / " +
                "PAYPAL_CLIENT_SECRET environment variables).");
        }
    }
}
