using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Strongly-typed PayPal settings, bound from the <c>PayPal:</c> configuration section. Values are
/// supplied via .NET user-secrets / environment configuration and are never stored in the repository.
/// Every part is <c>[Required]</c> (empty/whitespace rejected) so the host fails fast at startup rather
/// than discovering a missing credential as a 401 on the first call.
/// </summary>
public class PayPalOptions
{
    public const string SectionName = "PayPal";

    [Required]
    public string ClientId { get; set; } = string.Empty;

    [Required]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Target PayPal environment. Only <c>Sandbox</c> is supported by this SDK build.</summary>
    [Required]
    public string Environment { get; set; } = string.Empty;

    /// <summary>Three-letter ISO-4217 merchant currency used for every amount.</summary>
    [Required]
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional base-URL override. When set, it is used verbatim as the API base address for every PayPal
    /// call — including the OAuth token request — instead of the environment default.
    /// </summary>
    public string? BaseUrl { get; set; }
}
