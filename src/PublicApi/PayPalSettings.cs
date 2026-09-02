namespace Microsoft.eShopWeb.PublicApi;

/// <summary>
/// Settings bound from the "PayPal" configuration section. ClientId/ClientSecret come from
/// the secret store (user-secrets / environment); never hard-code them.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";

    /// <summary>Optional override used verbatim as the API base address for every PayPal call.</summary>
    public string? BaseUrl { get; set; }
}
