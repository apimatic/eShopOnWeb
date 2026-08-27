namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Bound from the "PayPal" configuration section. Values arrive via user-secrets or
/// environment-specific configuration — never hard-coded.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional verbatim override for the PayPal API base address, covering every call
    /// including the OAuth token request. When empty, the SDK derives it from Environment.
    /// </summary>
    public string? BaseUrl { get; set; }
}
