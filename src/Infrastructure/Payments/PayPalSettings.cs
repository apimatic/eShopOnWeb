using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Payments;

/// <summary>
/// PayPal integration settings, bound from the <c>PayPal:</c> configuration section. Values are
/// never hard-coded and never committed — they come from .NET user-secrets / environment.
/// </summary>
public class PayPalSettings
{
    public const string SectionName = "PayPal";

    [Required]
    public string? ClientId { get; set; }

    [Required]
    public string? ClientSecret { get; set; }

    [Required]
    public string? Environment { get; set; }

    [Required]
    public string? Currency { get; set; }

    /// <summary>
    /// Optional base-URL override. When set, it is used verbatim as the API base address for every
    /// PayPal call — including the OAuth token request — instead of the environment default.
    /// </summary>
    public string? BaseUrl { get; set; }
}
