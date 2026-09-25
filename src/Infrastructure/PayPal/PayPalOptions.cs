using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Settings bound from the <c>PayPal:</c> configuration section. Values come from configuration
/// (env vars → user-secrets); nothing is hard-coded. Validated at startup so a missing credential fails
/// the host boot rather than surfacing as a 401 on the first call.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    [Required(AllowEmptyStrings = false, ErrorMessage = "PayPal:ClientId is not configured.")]
    public string ClientId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false, ErrorMessage = "PayPal:ClientSecret is not configured.")]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Target PayPal environment. Only <c>sandbox</c> is supported by this SDK build.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "PayPal:Environment is not configured.")]
    public string Environment { get; set; } = "sandbox";

    /// <summary>ISO-4217 currency code applied to every payment.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "PayPal:Currency is not configured.")]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "PayPal:Currency must be a 3-letter ISO-4217 code.")]
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional API base-URL override. When set, it is used verbatim for every PayPal call — including the
    /// OAuth token request — instead of deriving one from <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Overall per-call budget (bounds the whole call incl. retries via a cancellation deadline).</summary>
    [Range(1, 600)]
    public int CallTimeoutSeconds { get; set; } = 30;
}
