using System;

namespace Microsoft.eShopWeb.ApplicationCore.Settings;

public class MaxioSettings
{
    public string ApiKey { get; set; } = null!;
    public string Subdomain { get; set; } = null!;
    public string Environment { get; set; } = "sandbox";
    public string? BaseUrl { get; set; }
    public string ProductFamilyHandle { get; set; } = null!;

    public string GetBaseUrl()
    {
        if (!string.IsNullOrEmpty(BaseUrl))
            return BaseUrl;

        if (Environment.Equals("sandbox", StringComparison.OrdinalIgnoreCase))
            return $"https://{Subdomain}.chargify.com";

        return $"https://{Subdomain}.ebilling.maxio.com";
    }
}
