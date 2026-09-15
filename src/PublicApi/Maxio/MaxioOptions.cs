using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public Uri GetBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain must be configured.");
        }

        return new Uri($"https://{Subdomain}.chargify.com/", UriKind.Absolute);
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("Maxio:ApiKey must be configured.");

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            throw new InvalidOperationException("Maxio:ProductFamilyHandle must be configured.");

        _ = GetBaseAddress();
    }
}
