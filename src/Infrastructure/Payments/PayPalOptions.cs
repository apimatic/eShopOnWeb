using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Strongly-typed PayPal settings bound from the <c>PayPal:</c> configuration section. Values are supplied
/// by configuration (user-secrets / environment) and never hard-coded — the same build must run against a
/// different PayPal account. Every credential is <see cref="RequiredAttribute"/> and validated on start so a
/// missing or blank value stops the host booting rather than surfacing as a 401 on the first call.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    [Required(AllowEmptyStrings = false, ErrorMessage = "PayPal:ClientId is not configured.")]
    public string ClientId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false, ErrorMessage = "PayPal:ClientSecret is not configured.")]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>The PayPal environment, e.g. <c>sandbox</c>. Bound from <c>PayPal:Environment</c>.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "PayPal:Environment is not configured.")]
    public string Environment { get; set; } = string.Empty;

    /// <summary>The ISO-4217 settlement currency, e.g. <c>USD</c>. Bound from <c>PayPal:Currency</c>.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "PayPal:Currency is not configured.")]
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional API base-URL override. When set, it is used verbatim as the base address for every PayPal
    /// call (including the OAuth token request) instead of deriving one from the environment.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsSandbox =>
        string.Equals(Environment, "sandbox", System.StringComparison.OrdinalIgnoreCase);
}
