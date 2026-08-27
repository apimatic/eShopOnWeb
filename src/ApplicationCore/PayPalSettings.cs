namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Bound from the "PayPal" configuration section. Values are supplied via user-secrets or
/// environment variables (PAYPAL_CLIENT_ID, PAYPAL_CLIENT_SECRET, PAYPAL_ENVIRONMENT,
/// PAYPAL_CURRENCY); none are hard-coded. BaseUrl is an optional override applied verbatim
/// to every PayPal call, including the OAuth token request.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";
    public string? BaseUrl { get; set; }
}
