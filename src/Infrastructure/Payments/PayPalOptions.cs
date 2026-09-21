using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal settings, bound from the <c>PayPal:</c> configuration section. Values are supplied by the
/// environment / user-secrets and never hard-coded. Every credential is <see cref="RequiredAttribute"/> so a
/// blank part fails startup validation (a blank part is not a missing one).
/// </summary>
public sealed class PayPalOptions
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
    /// Optional base-URL override. When set, it is used verbatim as the API base address for every PayPal
    /// call, including the OAuth token request. When unset, the SDK's sandbox host is used.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>True when <see cref="Environment"/> names the sandbox.</summary>
    public bool IsSandbox => string.Equals(Environment?.Trim(), "sandbox", System.StringComparison.OrdinalIgnoreCase);
}
