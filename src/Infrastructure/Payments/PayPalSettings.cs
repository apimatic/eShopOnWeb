namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal integration settings, bound from the <c>PayPal:</c> configuration section. Values are never
/// hard-coded — they come from configuration (user-secrets / environment) so the same build can run
/// against a different PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" (default) or "live"/"production".</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code used for all amounts, e.g. "USD".</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional base-URL override. When set, it is used verbatim as the API base address for every
    /// PayPal call, including the OAuth token request. When empty, the base URL is derived from
    /// <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsLive =>
        string.Equals(Environment, "live", System.StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Environment, "production", System.StringComparison.OrdinalIgnoreCase);
}
