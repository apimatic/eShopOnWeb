using System;
using Microsoft.eShopWeb.ApplicationCore.Exceptions;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;

    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var trimmed = BaseUrl.Trim().TrimEnd('/');
            return new Uri(trimmed + "/", UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured. Set Maxio:BaseUrl or Maxio:Subdomain (from MAXIO_SITE_SUBDOMAIN).");
        }

        return new Uri($"https://{Subdomain.Trim()}.chargify.com/", UriKind.Absolute);
    }

    public void EnsureApiKey()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured. Set Maxio:ApiKey (from MAXIO_API_KEY).");
        }
    }
}
