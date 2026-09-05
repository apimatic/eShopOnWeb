using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi;

/// <summary>Configuration for the Maxio Advanced Billing site.</summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    [Required]
    public string ApiKey { get; init; } = string.Empty;

    [Required]
    public string Subdomain { get; init; } = string.Empty;

    [Required]
    public string ProductFamilyHandle { get; init; } = string.Empty;

    /// <summary>Optional full API root. When empty, the US Advanced Billing site URL is derived from Subdomain.</summary>
    public string? BaseUrl { get; init; }

    public string GetBaseUrl()
    {
        // An explicit URL can point at a regional or proxy endpoint, so do not rewrite it.
        return string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com"
            : BaseUrl;
    }
}
