namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal transport configuration, bound from the "PayPal" configuration section.
/// Values are supplied through configuration / user-secrets and are never hard-coded.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>"sandbox" (default) or "live"/"production".</summary>
    public string? Environment { get; set; }

    /// <summary>ISO currency code used for every amount, e.g. "USD".</summary>
    public string? Currency { get; set; }

    /// <summary>
    /// Optional explicit API base address. When set it is used verbatim for every PayPal
    /// call — including the OAuth token request — instead of one derived from Environment.
    /// </summary>
    public string? BaseUrl { get; set; }
}
