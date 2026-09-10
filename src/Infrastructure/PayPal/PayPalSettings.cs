using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// PayPal settings, bound from the <c>PayPal:</c> configuration section. None of these values are
/// hard-coded; they come from configuration / user-secrets (populated from the PAYPAL_* env vars).
/// Validated at startup so the host refuses to boot on a missing or blank credential.
/// </summary>
public class PayPalSettings : IPaymentConfiguration
{
    public const string SectionName = "PayPal";

    [Required(AllowEmptyStrings = false, ErrorMessage = "PayPal:ClientId is not configured.")]
    public string ClientId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false, ErrorMessage = "PayPal:ClientSecret is not configured.")]
    public string ClientSecret { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false, ErrorMessage = "PayPal:Environment is not configured.")]
    public string Environment { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false, ErrorMessage = "PayPal:Currency is not configured.")]
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional base-URL override. When set, it is used verbatim as the API base address for every
    /// PayPal call — including the OAuth token request — instead of one derived from the environment.
    /// </summary>
    public string? BaseUrl { get; set; }
}
