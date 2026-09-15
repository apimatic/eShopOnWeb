using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public Uri GetBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var configuredBaseUrl) ||
                configuredBaseUrl.Scheme != Uri.UriSchemeHttps && configuredBaseUrl.Scheme != Uri.UriSchemeHttp)
            {
                throw new InvalidOperationException("Maxio:BaseUrl must be an absolute HTTP or HTTPS URL.");
            }

            return configuredBaseUrl.AbsoluteUri.EndsWith('/')
                ? configuredBaseUrl
                : new Uri(configuredBaseUrl.AbsoluteUri + "/");
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain is required when Maxio:BaseUrl is not configured.");
        }

        return new Uri($"https://{Subdomain}.chargify.com/");
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey)) throw new InvalidOperationException("Maxio:ApiKey is required.");
        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
            throw new InvalidOperationException("Maxio:Subdomain is required when Maxio:BaseUrl is not configured.");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle)) throw new InvalidOperationException("Maxio:ProductFamilyHandle is required.");

        _ = GetBaseAddress();
    }
}
