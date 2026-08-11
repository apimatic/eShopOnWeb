using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// Strongly-typed binding of the <c>PayPal:</c> configuration section. Also implements
/// <see cref="IPaymentSettings"/> so domain services can read the charge currency without
/// depending on the configuration provider.
/// </summary>
public class PayPalOptions : IPaymentSettings
{
    public const string SectionName = "PayPal";

    /// <summary>OAuth2 client-credentials client id.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OAuth2 client-credentials client secret.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Logical environment name (e.g. "Sandbox" / "Live"). Informational; host selection
    /// is driven by <see cref="BaseUrl"/> because the SDK exposes only a Sandbox environment.</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>ISO-4217 currency all amounts are charged in.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Optional verbatim base URL. When non-empty it overrides the host for EVERY call,
    /// including the OAuth2 token request. Leave empty to use the SDK's sandbox default.</summary>
    public string? BaseUrl { get; set; }

    /// <inheritdoc />
    public string CurrencyCode => Currency;
}
