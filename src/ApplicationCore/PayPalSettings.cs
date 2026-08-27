namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Settings bound from the "PayPal" configuration section. Values are supplied via
/// user-secrets / environment variables — never hard-coded or committed.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;

    /// <summary>Optional override; when set it is used verbatim as the API base address for every call.</summary>
    public string? BaseUrl { get; set; }
}
