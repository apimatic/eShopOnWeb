using System;

namespace Microsoft.eShopWeb.ApplicationCore.Billing;

public class MaxioSettings
{
    public const string SECTION_NAME = "Maxio";
    private const string US_HOST_SUFFIX = "chargify.com";
    private const string EU_HOST_SUFFIX = "ebilling.maxio.com";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? BaseUrl { get; set; }
    public string? ProductFamilyHandle { get; set; }
    public string Environment { get; set; } = "US";

    public Uri ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.TrimEnd('/'), UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio configuration is incomplete: set either Maxio:BaseUrl or Maxio:Subdomain.");
        }

        var suffix = string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase) ? EU_HOST_SUFFIX : US_HOST_SUFFIX;
        return new Uri($"https://{Subdomain}.{suffix}", UriKind.Absolute);
    }

    public string RequireApiKey()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("Maxio configuration is incomplete: Maxio:ApiKey is not set.");
        }

        return ApiKey;
    }

    public string RequireProductFamilyHandle()
    {
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio configuration is incomplete: Maxio:ProductFamilyHandle is not set.");
        }

        return ProductFamilyHandle;
    }
}
