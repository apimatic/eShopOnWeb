using System;

namespace Microsoft.eShopWeb.PublicApi;

public class MaxioConfiguration
{
    public const string SectionName = "Maxio";
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    public string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl;
        }

        if (!string.IsNullOrWhiteSpace(Subdomain))
        {
            return $"https://{Subdomain}.chargify.com";
        }

        throw new InvalidOperationException("Maxio configuration is incomplete. Either BaseUrl or Subdomain must be set.");
    }
}
