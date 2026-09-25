using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Settings bound from the <c>PayPal</c> configuration section. Values are supplied via
/// .NET user-secrets / environment variables — never committed to the repository.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    [Required(AllowEmptyStrings = false, ErrorMessage = "PayPal:ClientId is not configured.")]
    public string ClientId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false, ErrorMessage = "PayPal:ClientSecret is not configured.")]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Target environment; only the PayPal sandbox is supported by this SDK build.</summary>
    public string? Environment { get; set; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "PayPal:Currency is not configured.")]
    [StringLength(3, MinimumLength = 3, ErrorMessage = "PayPal:Currency must be a 3-letter ISO-4217 code.")]
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional base-URL override. When set, it is used verbatim as the API base address for every
    /// PayPal call, including the OAuth token request.
    /// </summary>
    public string? BaseUrl { get; set; }
}
