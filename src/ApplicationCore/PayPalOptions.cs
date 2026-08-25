namespace Microsoft.eShopWeb;

/// <summary>
/// Bound from the "PayPal" configuration section. Values come from environment/user-secrets in
/// every environment — never hardcoded.
/// </summary>
public class PayPalOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string Currency { get; set; } = "USD";

    /// <summary>Optional. When set, used verbatim as the API base address for every PayPal call,
    /// including the OAuth2 token request, instead of one derived from <see cref="Environment"/>.</summary>
    public string? BaseUrl { get; set; }
}
