using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Settings for the Maxio Advanced Billing site. These map directly to the Maxio
/// OpenAPI server template and HTTP basic authentication scheme.
/// </summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    [Required]
    public string Subdomain { get; init; } = string.Empty;

    [Required]
    public string ProductFamilyHandle { get; init; } = string.Empty;

    /// <summary>Optional complete API base address, useful for Maxio regional endpoints.</summary>
    public string? BaseUrl { get; init; }
}
