using System;

namespace Microsoft.eShopWeb.PublicApi.MaxioBilling;

public class MaxioBillingOptions
{
    public const string CONFIG_SECTION_NAME = "Maxio";

    public string? ApiKey { get; set; }

    public string? Subdomain { get; set; }

    public string? ProductFamilyHandle { get; set; }

    public string? BaseUrl { get; set; }

    public string Environment { get; set; } = "US";

    public string? ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl;
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            return null;
        }

        string host = string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? "ebilling.maxio.com"
            : "chargify.com";

        return $"https://{Subdomain}.{host}";
    }

    public bool IsConfigured
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ApiKey))
            {
                return false;
            }

            return ResolveBaseUrl() is not null;
        }
    }
}
