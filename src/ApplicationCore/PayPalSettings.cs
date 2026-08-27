namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Settings bound from the "PayPal" configuration section. Values are supplied
/// via environment variables / user-secrets and are never hard-coded.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live"; determines the PayPal base URL unless BaseUrl is set.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code used for all charges.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Optional override used verbatim as the API base address for every PayPal call.</summary>
    public string? BaseUrl { get; set; }
}
