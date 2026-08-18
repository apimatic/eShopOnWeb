using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;

    public string Subdomain { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public string? TryResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.Trim().TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            return null;
        }

        // OpenAPI servers: https://{site}.chargify.com (US default)
        return $"https://{Subdomain.Trim()}.chargify.com";
    }

    public string ResolveBaseUrl()
    {
        return TryResolveBaseUrl()
            ?? throw new MaxioConfigurationException("Configure Maxio:BaseUrl or Maxio:Subdomain.");
    }
}
