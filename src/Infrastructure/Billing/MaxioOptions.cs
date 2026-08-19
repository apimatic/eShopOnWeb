using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/') + "/";
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }

        return $"https://{Subdomain.Trim()}.chargify.com/";
    }

    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new ApplicationCore.Exceptions.BillingConfigurationException(
                "Maxio:ApiKey is not configured. Set MAXIO_API_KEY or the Maxio:ApiKey user secret.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new ApplicationCore.Exceptions.BillingConfigurationException(
                "Maxio:ProductFamilyHandle is not configured. Set MAXIO_DEFAULT_PRODUCT_FAMILY or the Maxio:ProductFamilyHandle user secret.");
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new ApplicationCore.Exceptions.BillingConfigurationException(
                "Maxio:Subdomain or Maxio:BaseUrl is required. Set MAXIO_SITE_SUBDOMAIN or Maxio:BaseUrl.");
        }
    }
}
