using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.PayPal;

/// <summary>
/// Settings bound from the <c>PayPal:</c> configuration section. Values are supplied via .NET
/// user-secrets / environment and are never committed to the repository. Every credential part is
/// <see cref="RequiredAttribute"/> so the host fails to start (with <c>ValidateOnStart</c>) when one
/// is missing or blank, rather than surfacing later as a 401 on the first call.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    [Required(AllowEmptyStrings = false)]
    public string ClientId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>The PayPal environment name (e.g. <c>sandbox</c>).</summary>
    [Required(AllowEmptyStrings = false)]
    public string Environment { get; set; } = string.Empty;

    /// <summary>The three-letter ISO-4217 currency to charge in.</summary>
    [Required(AllowEmptyStrings = false)]
    public string Currency { get; set; } = string.Empty;

    /// <summary>
    /// Optional base-URL override. When set, it is used verbatim as the API base address for every
    /// PayPal call — including the OAuth token request — instead of the one derived from the
    /// environment.
    /// </summary>
    public string? BaseUrl { get; set; }
}
