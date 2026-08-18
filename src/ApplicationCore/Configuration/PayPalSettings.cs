namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Strongly-typed PayPal integration settings, bound from the <c>PayPal:</c> configuration section.
/// Secret values are supplied via environment variables / .NET user-secrets and never committed.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";

    /// <summary>REST app client id (from <c>PAYPAL_CLIENT_ID</c>).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>REST app client secret (from <c>PAYPAL_CLIENT_SECRET</c>).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Target PayPal environment, e.g. <c>sandbox</c> (from <c>PAYPAL_ENVIRONMENT</c>).</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code used for every amount, e.g. <c>USD</c> (from <c>PAYPAL_CURRENCY</c>).</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional base-URL override. When set, it is used verbatim as the API base address for every PayPal
    /// call — including the OAuth token request — instead of one derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
