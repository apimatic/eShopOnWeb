using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioOptions
{
    public const string CONFIG_SECTION_NAME = "Maxio";

    public string ApiKey { get; set; } = string.Empty;

    public string Subdomain { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public string Environment { get; set; } = string.Empty;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(Subdomain) &&
        !string.IsNullOrWhiteSpace(ProductFamilyHandle);

    public Uri ResolveBaseUri()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.TrimEnd('/'), UriKind.Absolute);
        }

        string host = string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? "ebilling.maxio.com"
            : "chargify.com";

        return new Uri($"https://{Subdomain}.{host}", UriKind.Absolute);
    }
}
