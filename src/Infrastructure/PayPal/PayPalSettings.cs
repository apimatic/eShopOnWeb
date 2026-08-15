namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Options bound from the "PayPal" configuration section. Secrets (<see cref="ClientId"/> /
/// <see cref="ClientSecret"/>) come from configuration/secret store, never source.
/// </summary>
public sealed class PayPalSettings
{
    /// <summary>OAuth2 client-credentials client id.</summary>
    public string ClientId { get; set; } = default!;

    /// <summary>OAuth2 client-credentials client secret.</summary>
    public string ClientSecret { get; set; } = default!;

    /// <summary>
    /// Named environment. The SDK only exposes a "Sandbox" server environment; this value is kept
    /// for configuration clarity and future use. A live/production host is reached via
    /// <see cref="BaseUrl"/>, not by switching this string.
    /// </summary>
    public string Environment { get; set; } = "Sandbox";

    /// <summary>ISO-4217 currency code used for order amounts (e.g. "USD").</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional verbatim base-URL override applied to ALL calls including the OAuth token request.
    /// When null/empty the SDK's default sandbox host is used. A bare origin (scheme+host[+port]),
    /// no trailing path.
    /// </summary>
    public string? BaseUrl { get; set; }
}
