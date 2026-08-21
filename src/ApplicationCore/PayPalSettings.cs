namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the "PayPal" configuration section.
/// Values are supplied through configuration / user-secrets and are never hard-coded.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    /// <summary>REST app client id (from PAYPAL_CLIENT_ID).</summary>
    public string? ClientId { get; set; }

    /// <summary>REST app client secret (from PAYPAL_CLIENT_SECRET).</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Target environment: "sandbox" or "live" (from PAYPAL_ENVIRONMENT).</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code applied to every amount (from PAYPAL_CURRENCY).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base URL. When set it is used verbatim for every PayPal call
    /// (including the OAuth token request), overriding the environment-derived host.
    /// </summary>
    public string? BaseUrl { get; set; }
}
