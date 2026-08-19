using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;

    public bool TryResolveBaseUrl(out string baseUrl)
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            baseUrl = NormalizeBaseUrl(BaseUrl);
            return true;
        }

        if (!string.IsNullOrWhiteSpace(Subdomain))
        {
            baseUrl = $"https://{Subdomain.Trim()}.chargify.com/";
            return true;
        }

        baseUrl = string.Empty;
        return false;
    }

    public string ResolveBaseUrl()
    {
        if (TryResolveBaseUrl(out var baseUrl))
        {
            return baseUrl;
        }

        throw new MaxioConfigurationException(
            "Maxio is not configured. Set Maxio:BaseUrl or Maxio:Subdomain (from MAXIO_SITE_SUBDOMAIN).");
    }

    public void EnsureConfiguredForRequests()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new MaxioConfigurationException(
                "Maxio:ApiKey is not configured. Set it from the MAXIO_API_KEY environment variable via user-secrets or environment configuration.");
        }

        _ = ResolveBaseUrl();

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new MaxioConfigurationException(
                "Maxio:ProductFamilyHandle is not configured. Set it from the MAXIO_DEFAULT_PRODUCT_FAMILY environment variable.");
        }
    }

    private static string NormalizeBaseUrl(string value)
    {
        var trimmed = value.Trim();
        return trimmed.EndsWith('/') ? trimmed : trimmed + "/";
    }
}
