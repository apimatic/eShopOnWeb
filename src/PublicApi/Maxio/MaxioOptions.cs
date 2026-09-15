using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }
    public string? BaseUrl { get; set; }

    public Uri GetApiBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain is not configured.");
        }

        return new Uri($"https://{Subdomain}.chargify.com/", UriKind.Absolute);
    }

    public void EnsureCredentials()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is not configured.");
        }

        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
        {
            throw new InvalidOperationException("Configure Maxio:Subdomain or Maxio:BaseUrl.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured.");
        }
    }
}
