namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Binds the <c>PayPal:</c> configuration section. Values come from user-secrets / environment and
/// are never hard-coded, so the same build runs against a different PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    /// <summary>REST client id (from PAYPAL_CLIENT_ID).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>REST client secret (from PAYPAL_CLIENT_SECRET).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Target environment name (from PAYPAL_ENVIRONMENT), e.g. "sandbox".</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO currency code every amount is expressed in (from PAYPAL_CURRENCY).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional API base-URL override. When set, it is used verbatim as the base address for every
    /// PayPal call — including the OAuth token request — instead of one derived from the environment.
    /// </summary>
    public string? BaseUrl { get; set; }
}
