namespace Microsoft.eShopWeb;

/// <summary>
/// Settings bound from the "PayPal" configuration section.
/// Values are supplied via user-secrets or the PAYPAL_CLIENT_ID / PAYPAL_CLIENT_SECRET /
/// PAYPAL_ENVIRONMENT / PAYPAL_CURRENCY environment variables - never hard-coded.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional override. When set, used verbatim as the API base address for every
    /// PayPal call, including the OAuth token request.
    /// </summary>
    public string? BaseUrl { get; set; }
}
