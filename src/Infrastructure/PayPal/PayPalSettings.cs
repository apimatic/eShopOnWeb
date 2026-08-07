namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the "PayPal" configuration section. Secret values
/// (<see cref="ClientId"/>, <see cref="ClientSecret"/>) are expected to come from user-secrets / environment
/// variables and must never be hard-coded.
/// </summary>
public class PayPalSettings
{
    /// <summary>OAuth2 client id (PayPal REST app).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OAuth2 client secret (PayPal REST app).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Target environment. Only "sandbox" is supported by this SDK build.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>
    /// Optional base-URL override. When non-empty it is used verbatim as the API base address instead of the
    /// environment default (e.g. to point at a mock server or proxy).
    /// </summary>
    public string? BaseUrl { get; set; }
}
