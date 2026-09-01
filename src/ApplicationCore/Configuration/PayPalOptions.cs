namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Bound from the "PayPal" configuration section. Values arrive via
/// user-secrets or environment variables — never from files in the repo.
/// </summary>
public class PayPalOptions
{
    public const string ConfigName = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Environment { get; set; }
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional override used verbatim as the API base address for every
    /// PayPal call, including the OAuth token request.
    /// </summary>
    public string? BaseUrl { get; set; }
}
