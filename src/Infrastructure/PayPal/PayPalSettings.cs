using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the <c>PayPal:</c> section with exactly these keys.
/// Values are supplied by configuration (user-secrets / environment) and never hard-coded, so the same
/// build runs against a different PayPal account by changing configuration alone. Validated at startup
/// (fail-fast) so a missing or blank credential stops the host rather than surfacing as a later 401.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    /// <summary>PayPal:ClientId (from PAYPAL_CLIENT_ID).</summary>
    [Required(AllowEmptyStrings = false)]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>PayPal:ClientSecret (from PAYPAL_CLIENT_SECRET).</summary>
    [Required(AllowEmptyStrings = false)]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>PayPal:Environment (from PAYPAL_ENVIRONMENT) — only "sandbox" is supported by this SDK.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Environment { get; set; } = string.Empty;

    /// <summary>PayPal:Currency (from PAYPAL_CURRENCY) — ISO-4217 code used for all amounts.</summary>
    [Required(AllowEmptyStrings = false)]
    [StringLength(3, MinimumLength = 3)]
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// PayPal:BaseUrl — optional override. When set, it is used verbatim as the API base address for every
    /// PayPal call (including the credential/token request) instead of one derived from the environment.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>PayPal:CallTimeoutSeconds — the whole-call budget for each PayPal request. Default 30s.</summary>
    [Range(1, 600)]
    public int CallTimeoutSeconds { get; set; } = 30;
}
