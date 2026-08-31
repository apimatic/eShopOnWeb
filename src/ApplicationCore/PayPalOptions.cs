namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Bound from the "PayPal" configuration section. ClientId/ClientSecret come from user-secrets or
/// environment-specific configuration — never from files committed to the repository.
/// BaseUrl is an optional override used verbatim as the API base address for every PayPal call
/// (including the token request) instead of deriving it from Environment.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
}
