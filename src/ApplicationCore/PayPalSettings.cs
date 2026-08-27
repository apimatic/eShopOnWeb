using System;

namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Bound from the "PayPal" configuration section. Values arrive via user-secrets or
/// environment variables; none are hard-coded. BaseUrl is an optional override that,
/// when set, is used verbatim for every PayPal call including the token request.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";
    public string? BaseUrl { get; set; }

    /// <summary>
    /// The API base address. The sandbox URL is the server declared in the PayPal OpenAPI
    /// specifications; "live"/"production" selects the corresponding live host.
    /// </summary>
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
}
