using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Bound from the <c>PayPal:</c> configuration section. Values come from user-secrets /
/// environment (PAYPAL_CLIENT_ID etc.) and are never hard-coded or committed.
/// </summary>
public class PayPalSettings : IPaymentSettings
{
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>"sandbox" or "live"/"production". Selects the default API host.</summary>
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency all amounts are charged in.</summary>
    public string Currency { get; set; } = "USD";

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base address for every
    /// PayPal call (including the token request) instead of one derived from Environment.
    /// </summary>
    public string? BaseUrl { get; set; }
}
