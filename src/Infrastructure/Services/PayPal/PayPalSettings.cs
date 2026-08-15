using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Services.PayPal;

/// <summary>
/// PayPal integration settings, bound from the <c>PayPal:</c> configuration section. Values are supplied
/// at runtime (env vars → user-secrets) and are never hard-coded or committed.
/// </summary>
public class PayPalSettings : IPaymentOptions
{
    public const string SectionName = "PayPal";

    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }

    /// <summary>PayPal environment; only <c>sandbox</c> is targeted here.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code order amounts are charged in.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional base-URL override. When set it is used verbatim for every PayPal call, including the
    /// OAuth2 token request. When null the SDK's sandbox default is used.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
