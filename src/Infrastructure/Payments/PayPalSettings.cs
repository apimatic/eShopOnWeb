namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal configuration, bound from the "PayPal" configuration section. Values are supplied via
/// environment variables / user-secrets — never hard-coded in the repository.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Target PayPal environment. Only "sandbox" is supported for this app.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>
    /// Optional API base-URL override. When set, it is used verbatim as the API base address instead
    /// of the default derived from <see cref="Environment"/> (sandbox → https://api-m.sandbox.paypal.com).
    /// </summary>
    public string? BaseUrl { get; set; }
}
