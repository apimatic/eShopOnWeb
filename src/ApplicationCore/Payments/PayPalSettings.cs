namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// PayPal integration settings bound from the <c>PayPal:</c> configuration section. Values are loaded
/// from environment/user-secrets — never hard-coded — so the same build can run against a different
/// PayPal account.
/// </summary>
public sealed class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>PayPal environment name (e.g. "sandbox").</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO currency code amounts are charged in (e.g. "USD").</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional verbatim API base-URL override. When set, it is used exactly as given for every PayPal
    /// call — including the OAuth token request — instead of deriving one from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
