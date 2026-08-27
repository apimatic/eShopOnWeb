namespace Microsoft.eShopWeb;

/// <summary>
/// Settings bound from the "PayPal" configuration section. Values arrive via
/// environment variables / user-secrets; none are hard-coded.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Environment { get; set; }
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base address
    /// for every PayPal call, including the OAuth token request.
    /// </summary>
    public string? BaseUrl { get; set; }
}
