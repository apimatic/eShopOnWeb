using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// Secrets (ApiKey) are supplied via user-secrets or environment variables, never via appsettings files.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    [Required]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Site subdomain, e.g. "mysite" for https://mysite.chargify.com.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family that contains the subscription plans offered in the shop.
    /// </summary>
    [Required]
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim
    /// instead of deriving the address from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return $"https://{Subdomain}.chargify.com";
    }
}
