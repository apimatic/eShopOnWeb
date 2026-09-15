namespace Microsoft.eShopWeb.ApplicationCore.Payments;

/// <summary>
/// Strongly-typed PayPal settings, bound from the "PayPal" configuration section.
/// Values are supplied via environment variables / user-secrets and are never committed to the repo.
/// </summary>
public class PayPalOptions
{
    public const string CONFIG_SECTION = "PayPal";

    /// <summary>REST app client id (from PAYPAL_CLIENT_ID).</summary>
    public string? ClientId { get; set; }

    /// <summary>REST app client secret (from PAYPAL_CLIENT_SECRET).</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Target PayPal environment, e.g. "sandbox" or "live" (from PAYPAL_ENVIRONMENT).</summary>
    public string? Environment { get; set; }

    /// <summary>ISO-4217 currency code amounts are expressed in (from PAYPAL_CURRENCY), e.g. "USD".</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Optional API base-URL override. When set, it must be used verbatim as the base address for
    /// every PayPal call (including the OAuth2 token request) instead of one derived from the environment.
    /// </summary>
    public string? BaseUrl { get; set; }
}
