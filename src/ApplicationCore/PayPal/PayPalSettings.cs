namespace Microsoft.eShopWeb.ApplicationCore.PayPal;

/// <summary>
/// PayPal integration settings, bound from the "PayPal" configuration section. None of these values
/// are hard-coded anywhere in the build — the credentials come from user-secrets / environment, so
/// the same build can run against a different PayPal account.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";

    /// <summary>REST client id of the sandbox/live business account (PayPal:ClientId).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>REST client secret (PayPal:ClientSecret).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Target environment, e.g. "sandbox" (PayPal:Environment).</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency for every amount, e.g. "USD" (PayPal:Currency).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional base-URL override (PayPal:BaseUrl). When set, it is used verbatim as the API base for
    /// every PayPal call — including the OAuth token request — instead of the environment default.
    /// </summary>
    public string? BaseUrl { get; set; }
}
