namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Bound from the "PayPal" configuration section. Values arrive via
/// user-secrets / environment — never from files committed to the repo.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional override: when set, used verbatim as the API base address for
    /// every PayPal call, including the credential/token request.
    /// </summary>
    public string? BaseUrl { get; set; }
}
