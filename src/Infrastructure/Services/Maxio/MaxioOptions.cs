using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// Strongly-typed Maxio Advanced Billing settings, bound from the "Maxio" configuration section.
/// Values are supplied via environment variables / user-secrets and must never be committed to
/// the repository; only the key names live in source.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>Site-scoped Advanced Billing API key (used as the HTTP Basic username).</summary>
    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain, e.g. "cp-exp-8" for https://cp-exp-8.chargify.com.</summary>
    [Required]
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are offered as subscription plans.</summary>
    [Required]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim override for the API base address. When set, it is used exactly as given
    /// instead of deriving https://{Subdomain}.chargify.com from the subdomain/environment.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Maxio data-center region: "US" (default) or "EU". Only used when BaseUrl is not set.</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Plan handle used when a subscribe request does not specify one. Optional; when unset a
    /// request without a plan handle is rejected with guidance listing the available plans.
    /// </summary>
    public string? DefaultPlanHandle { get; set; }
}
