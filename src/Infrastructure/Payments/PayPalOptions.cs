using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal integration settings, bound from the <c>PayPal:</c> configuration section. Values are supplied
/// via configuration (user-secrets / environment) and never hard-coded — the same build must run against
/// a different PayPal account.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    /// <summary>From <c>PAYPAL_CLIENT_ID</c> → <c>PayPal:ClientId</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>From <c>PAYPAL_CLIENT_SECRET</c> → <c>PayPal:ClientSecret</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>From <c>PAYPAL_ENVIRONMENT</c> → <c>PayPal:Environment</c> (e.g. "sandbox").</summary>
    [Required(AllowEmptyStrings = false)]
    public string Environment { get; set; } = string.Empty;

    /// <summary>From <c>PAYPAL_CURRENCY</c> → <c>PayPal:Currency</c> (ISO-4217, e.g. "USD").</summary>
    [Required(AllowEmptyStrings = false)]
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional base-URL override (<c>PayPal:BaseUrl</c>). When set, it is used verbatim as the API base
    /// address for every PayPal call, including the OAuth token request. When absent, the SDK's default
    /// sandbox base URL is used.
    /// </summary>
    public string? BaseUrl { get; set; }
}
