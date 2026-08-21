namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Strongly-typed PayPal settings, bound from the <c>PayPal:</c> configuration section. Values are
/// supplied by configuration / user-secrets / environment only — never hard-coded — so the same
/// build can run against a different PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string ConfigSection = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Target environment (e.g. "sandbox").</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>3-letter ISO currency code the order total is charged in.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit base URL. When set, it is used verbatim as the API base address for every
    /// PayPal call (including the OAuth2 token request), instead of one derived from the environment.
    /// </summary>
    public string? BaseUrl { get; set; }
}
