namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the <c>PayPal:</c> configuration section. Values are
/// never hard-coded; they come from configuration / user-secrets (fed from the PAYPAL_* environment
/// variables) so the same build can run against a different PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>e.g. "sandbox" or "live".</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code used for all amounts, e.g. "USD".</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base URL. When set, it is used verbatim for every PayPal call
    /// (including the OAuth token request) instead of deriving one from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
