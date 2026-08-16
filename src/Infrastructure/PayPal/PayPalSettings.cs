using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal integration settings, bound from the <c>PayPal:</c> configuration section. Values are
/// never hard-coded — they come from configuration/user-secrets so the same build can run against
/// a different PayPal account.
/// </summary>
public class PayPalSettings : IPaymentConfiguration
{
    public const string SectionName = "PayPal";

    /// <summary>PayPal REST client id (from <c>PayPal:ClientId</c>).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>PayPal REST client secret (from <c>PayPal:ClientSecret</c>).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Target environment name, e.g. "sandbox" (from <c>PayPal:Environment</c>).</summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>ISO-4217 currency for all charges (from <c>PayPal:Currency</c>).</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional base-URL override (from <c>PayPal:BaseUrl</c>). When set, it is used verbatim as
    /// the API base address for every PayPal call, including the OAuth token request.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsSandbox =>
        string.Equals(Environment, "sandbox", System.StringComparison.OrdinalIgnoreCase);
}
