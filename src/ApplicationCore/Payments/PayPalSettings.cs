namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the <c>PayPal:</c> section. Values are supplied
/// via environment variables / user-secrets and never hard-coded, so the same build runs against a
/// different PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    /// <summary>REST client id (from PAYPAL_CLIENT_ID).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>REST client secret (from PAYPAL_CLIENT_SECRET).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Target environment, e.g. "sandbox" or "production" (from PAYPAL_ENVIRONMENT).</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency for all payments (from PAYPAL_CURRENCY).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base address. When set, it is used verbatim as the base URL for EVERY
    /// PayPal call — including the credential/token request — instead of deriving one from the
    /// environment.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsSandbox =>
        !string.Equals(Environment, "production", System.StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(Environment, "live", System.StringComparison.OrdinalIgnoreCase);
}
