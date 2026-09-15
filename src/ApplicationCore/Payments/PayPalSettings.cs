namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// PayPal integration settings, bound from the "PayPal" configuration section. None of these values are
/// hard-coded — they come from configuration (user-secrets / environment) so the same build can run against
/// a different PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Expected "sandbox". The SDK build exposes only the sandbox server environment.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code applied to every amount, e.g. "USD".</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional API base-URL override. When set, it is used verbatim as the base address for every PayPal
    /// call — including the OAuth token request — instead of the SDK's default sandbox host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
