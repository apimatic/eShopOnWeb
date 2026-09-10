using System.ComponentModel.DataAnnotations;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal settings, bound from the <c>PayPal:</c> configuration section. The values are supplied via
/// .NET user-secrets (loaded from environment variables); none are hard-coded, so the same build runs
/// against a different PayPal account. Each required part is validated at startup (fail-fast).
/// </summary>
public class PayPalOptions : IPaymentSettings
{
    public const string SectionName = "PayPal";

    /// <summary>From <c>PAYPAL_CLIENT_ID</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>From <c>PAYPAL_CLIENT_SECRET</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>From <c>PAYPAL_ENVIRONMENT</c> (e.g. "sandbox").</summary>
    [Required(AllowEmptyStrings = false)]
    public string Environment { get; set; } = string.Empty;

    /// <summary>From <c>PAYPAL_CURRENCY</c> (ISO-4217, e.g. "USD").</summary>
    [Required(AllowEmptyStrings = false)]
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set, used verbatim as the API base address for every PayPal call —
    /// including the credential/token request — instead of deriving one from the environment.
    /// </summary>
    public string? BaseUrl { get; set; }
}
