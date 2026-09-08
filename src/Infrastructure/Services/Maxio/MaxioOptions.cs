using System;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public const string EnvApiKey = "MAXIO_API_KEY";
    public const string EnvSubdomain = "MAXIO_SITE_SUBDOMAIN";
    public const string EnvProductFamilyHandle = "MAXIO_DEFAULT_PRODUCT_FAMILY";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override for the API base address. When set it is used verbatim.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolveApiBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                $"Maxio:{nameof(Subdomain)} is not configured. Set the {EnvSubdomain} environment variable or provide Maxio:BaseUrl.");
        }

        return $"https://{Subdomain!.Trim().TrimEnd('.')}.chargify.com";
    }
}
