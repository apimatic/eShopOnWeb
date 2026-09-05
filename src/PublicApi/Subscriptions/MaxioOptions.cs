using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>
/// Connection settings for the Maxio Advanced Billing API.
/// Values belong in the PublicApi user-secret store or the deployment's configuration provider.
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

    /// <summary>
    /// Optional absolute API base address. When omitted, the US Maxio server from the OpenAPI contract is used.
    /// </summary>
    public string? BaseUrl { get; init; }
}
