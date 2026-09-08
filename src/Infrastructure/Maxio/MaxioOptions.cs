using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration. Bound from the "Maxio" configuration
/// section. Secrets are supplied via user-secrets or environment variables, never committed.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    [Required]
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Maxio Advanced Billing hosting region: US or EU.</summary>
    public string Environment { get; set; } = "US";

    /// <summary>Handle of the product family that contains the subscription plans.</summary>
    [Required]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base address instead of
    /// deriving one from the subdomain and environment.
    /// </summary>
    public string? BaseUrl { get; set; }
}
