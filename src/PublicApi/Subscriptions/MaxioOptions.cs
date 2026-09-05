using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    [Required]
    public string Subdomain { get; init; } = string.Empty;

    [Required]
    public string ProductFamilyHandle { get; init; } = string.Empty;

    /// <summary>Optional API base-address override. When absent, the US server from the supplied Maxio OpenAPI contract is used.</summary>
    public string? BaseUrl { get; init; }
}
