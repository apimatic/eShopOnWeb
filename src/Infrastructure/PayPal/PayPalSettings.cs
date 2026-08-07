namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal configuration, bound from the <c>PayPal</c> configuration section. Values are supplied via
/// configuration / user-secrets (never hard-coded): <c>PayPal:ClientId</c>, <c>PayPal:ClientSecret</c>,
/// <c>PayPal:Environment</c> and the optional <c>PayPal:BaseUrl</c> override.
/// </summary>
public class PayPalSettings
{
    public const string CONFIG_NAME = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Target PayPal environment. Only <c>sandbox</c> is supported by this integration.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>
    /// Optional API base-URL override. When set it is used verbatim as the API base address instead of the
    /// address derived from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
