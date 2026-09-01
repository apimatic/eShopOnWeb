namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Bound from the "PayPal" configuration section. Values are supplied via user-secrets or
/// environment variables — never from files in the repository.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional override. When set, used verbatim as the API base address for every PayPal
    /// call, including the credential/token request.
    /// </summary>
    public string? BaseUrl { get; set; }
}
