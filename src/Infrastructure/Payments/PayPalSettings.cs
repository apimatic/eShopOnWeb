using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// Strongly-typed PayPal configuration, bound from the <c>PayPal:</c> section. Values come from
/// configuration only (env vars → user-secrets) and are never hard-coded. <see cref="BaseUrl"/> is an
/// optional override used verbatim for every PayPal call when set.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    [Required(AllowEmptyStrings = false)]
    public string ClientId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ClientSecret { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string Environment { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string Currency { get; set; } = string.Empty;

    /// <summary>Optional base-URL override; when set, used verbatim as the API base for every call
    /// (including the token request).</summary>
    public string? BaseUrl { get; set; }
}
