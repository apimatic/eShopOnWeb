namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Settings bound from the "PayPal" configuration section:
/// PayPal:ClientId, PayPal:ClientSecret, PayPal:Environment, PayPal:Currency, PayPal:BaseUrl.
/// Values are supplied via user-secrets / environment variables, never hard-coded.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live". Used to derive the API base address when BaseUrl is not set.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code used for all charges (e.g. "USD").</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional override. When set, used verbatim as the API base address for every
    /// PayPal call, including the credential/token request.
    /// </summary>
    public string? BaseUrl { get; set; }
}
