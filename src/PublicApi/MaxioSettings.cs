using System;

namespace Microsoft.eShopWeb.PublicApi;

public class MaxioSettings
{
    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }
    public string? BaseUrl { get; set; }
    public string? Environment { get; set; }

    public string GetBaseUrl()
    {
        if (!string.IsNullOrEmpty(BaseUrl))
            return BaseUrl;

        if (string.IsNullOrEmpty(Subdomain))
            throw new InvalidOperationException("Maxio Subdomain is not configured");

        return $"https://{Subdomain}.chargify.com";
    }
}
