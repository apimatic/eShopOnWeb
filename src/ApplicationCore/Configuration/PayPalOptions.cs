namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Strongly-typed binding of the <c>PayPal:</c> configuration section. Values are supplied through
/// configuration / user-secrets and are never hard-coded, so the same build can target a different
/// PayPal account. <see cref="BaseUrl"/> is an optional verbatim override for the API base address
/// (including the OAuth token call); when empty the base address is derived from <see cref="Environment"/>.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>e.g. "sandbox" (the only environment this SDK exposes) or "live".</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code used for every amount, e.g. "USD".</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>Optional verbatim base-URL override. When set it is used for every PayPal call.</summary>
    public string? BaseUrl { get; set; }
}
