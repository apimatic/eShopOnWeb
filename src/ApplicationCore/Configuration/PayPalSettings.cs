namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Bound from the <c>PayPal:</c> configuration section. Values are supplied via environment/user-secrets
/// and are never hard-coded, so the same build can run against a different PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>e.g. "sandbox". Currently the SDK only ships a Sandbox environment.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code amounts are charged in, e.g. "USD".</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional API base-URL override. When set, it is used verbatim as the base address for every
    /// PayPal call — including the OAuth token request — instead of one derived from the environment.
    /// </summary>
    public string? BaseUrl { get; set; }
}
