using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Bound from the <c>PayPal:</c> configuration section. Every value comes from configuration
/// (env vars → user-secrets); none is hard-coded, so the same build runs against a different PayPal
/// account. Credentials are validated at startup (see <c>AddPayPalIntegration</c>) so a missing
/// secret stops the host booting rather than surfacing as a 401 on the first live call.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    /// <summary>From <c>PAYPAL_CLIENT_ID</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>From <c>PAYPAL_CLIENT_SECRET</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>From <c>PAYPAL_ENVIRONMENT</c> (e.g. <c>sandbox</c>). Only the sandbox host exists in this SDK.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Environment { get; set; } = string.Empty;

    /// <summary>From <c>PAYPAL_CURRENCY</c> (ISO-4217, e.g. <c>USD</c>). All order amounts use this currency.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional override (<c>PayPal:BaseUrl</c>). When set, it is used verbatim as the API base address for
    /// every PayPal call — including the OAuth2 token request — instead of the environment default.
    /// </summary>
    public string? BaseUrl { get; set; }
}
