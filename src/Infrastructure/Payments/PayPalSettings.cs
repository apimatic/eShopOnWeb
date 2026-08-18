using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal settings bound from the <c>PayPal:</c> configuration section. No values are hard-coded —
/// the same build runs against a different account by changing configuration/user-secrets.
/// </summary>
public class PayPalSettings : IPaymentConfiguration
{
    public const string SectionName = "PayPal";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Informational; this SDK build targets the PayPal sandbox.</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>ISO-4217 currency code for all amounts.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional base-URL override. When set, it is used verbatim for every PayPal call — including the
    /// OAuth/token request — instead of the SDK's default sandbox host.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// When true, logs the PayPal response status and body (never request bodies, which carry card data)
    /// at Information level. For first-run wire verification/diagnostics only; off by default.
    /// </summary>
    public bool WireLog { get; set; }

    public string CurrencyCode => Currency;
}
