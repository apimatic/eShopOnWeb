using System;

namespace Microsoft.eShopWeb.PublicApi.MaxioBilling;

public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            return BaseUrl.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(Subdomain))
            throw new InvalidOperationException("Maxio Subdomain is not configured.");

        return $"https://{Subdomain}.chargify.com";
    }
}
