using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MaxioOptions
{
    public string ApiKey { get; set; } = "";
    public string Subdomain { get; set; } = "";
    public string ProductFamilyHandle { get; set; } = "";
    public string BaseUrl { get; set; } = "";

    public string GetBaseUrl()
    {
        if (!string.IsNullOrEmpty(BaseUrl))
        {
            return BaseUrl;
        }

        if (!string.IsNullOrEmpty(Subdomain))
        {
            return $"https://{Subdomain}.chargify.com";
        }

        throw new InvalidOperationException("Either Maxio:BaseUrl or Maxio:Subdomain must be configured");
    }
}
