namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Bound from the <c>PayPal</c> configuration section. Values are supplied via user-secrets / environment
/// and are never committed to the repository.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    /// <summary>REST client id of the sandbox/live business account (from PayPal:ClientId).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>REST client secret (from PayPal:ClientSecret).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Target environment name, e.g. "sandbox" (from PayPal:Environment).</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency for order amounts, e.g. "USD" (from PayPal:Currency).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional explicit API base URL (from PayPal:BaseUrl). When set, it is used verbatim for every PayPal
    /// call — including the token request — instead of deriving one from the environment.
    /// </summary>
    public string? BaseUrl { get; set; }
}
