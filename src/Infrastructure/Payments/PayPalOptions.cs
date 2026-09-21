using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Strongly-typed PayPal settings, bound from the <c>PayPal:</c> configuration section. Every value
/// is supplied by configuration (user-secrets / environment) — none is hard-coded — so the same build
/// runs against a different PayPal account by changing configuration alone. The host validates these
/// on start and refuses to boot if a credential is missing or blank.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    /// <summary>From PAYPAL_CLIENT_ID.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "PayPal:ClientId is not configured.")]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>From PAYPAL_CLIENT_SECRET.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "PayPal:ClientSecret is not configured.")]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>From PAYPAL_ENVIRONMENT (e.g. "sandbox"). The SDK exposes only a Sandbox environment.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "PayPal:Environment is not configured.")]
    public string Environment { get; set; } = string.Empty;

    /// <summary>From PAYPAL_CURRENCY (ISO-4217, e.g. "USD"). Amounts settle in this currency.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "PayPal:Currency is not configured.")]
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional base-URL override. When set, it is used verbatim as the API base for every PayPal
    /// call, including the OAuth token request. When unset, the SDK's sandbox default is used.
    /// </summary>
    public string? BaseUrl { get; set; }
}
