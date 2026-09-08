using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public string? DefaultProductHandle { get; set; }
    public string? Environment { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio is not configured: set either 'Maxio:BaseUrl' or 'Maxio:Subdomain'.");
        }

        return $"https://{Subdomain}.chargify.com";
    }
}
