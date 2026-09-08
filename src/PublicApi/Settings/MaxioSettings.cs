using System;

namespace Microsoft.eShopWeb.PublicApi.Settings;

public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string Environment { get; set; } = "US";
    public string BaseUrl { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Subdomain);

    public string GetApiBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        string host = string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? "ebilling.maxio.com"
            : "chargify.com";
        return $"https://{Subdomain}.{host}";
    }

    public string GetProductFamilyPathSegment()
    {
        string handle = ProductFamilyHandle.Trim();
        if (handle.StartsWith("handle:", StringComparison.OrdinalIgnoreCase) ||
            long.TryParse(handle, out _))
        {
            return handle;
        }

        return $"handle:{handle}";
    }
}
