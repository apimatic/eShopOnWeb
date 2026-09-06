using System;

namespace Microsoft.eShopWeb.ApplicationCore;

public class MaxioConfiguration
{
    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }
    public string? BaseUrl { get; set; }

    public string GetBaseUrl()
    {
        if (!string.IsNullOrEmpty(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrEmpty(Subdomain))
        {
            throw new InvalidOperationException("Either BaseUrl or Subdomain must be configured");
        }

        return $"https://{Subdomain}.chargify.com";
    }
}
