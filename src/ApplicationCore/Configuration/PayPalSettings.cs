namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Settings bound from the "PayPal" configuration section. Values are supplied via
/// environment variables / user-secrets (PAYPAL_CLIENT_ID, PAYPAL_CLIENT_SECRET,
/// PAYPAL_ENVIRONMENT, PAYPAL_CURRENCY); BaseUrl is an optional override that, when set,
/// is used verbatim as the API base address for every PayPal call including the token request.
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
