using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Settings bound from the <c>PayPal:</c> configuration section. Values are supplied by
/// configuration (env vars → user-secrets) and are never hard-coded. Every part is
/// <see cref="RequiredAttribute"/> so the host fails fast at startup if one is missing/blank
/// rather than surfacing a 401 on the first call.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    [Required(AllowEmptyStrings = false)]
    public string ClientId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Target PayPal environment, e.g. <c>sandbox</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Environment { get; set; } = string.Empty;

    /// <summary>ISO-4217 currency code for all amounts, e.g. <c>USD</c>.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional base-URL override. When set it is used verbatim as the API base address for every
    /// PayPal call (including the token request), instead of one derived from the environment.
    /// </summary>
    public string? BaseUrl { get; set; }
}
