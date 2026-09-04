using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }
    public string? BaseUrl { get; set; }

    public Uri GetBaseAddress()
    {
        var configuredBaseUrl = BaseUrl?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredBaseUrl))
        {
            if (!Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var baseAddress)
                || baseAddress.Scheme is not ("http" or "https"))
            {
                throw new InvalidOperationException("Maxio:BaseUrl must be an absolute HTTP or HTTPS URL.");
            }

            return new Uri(configuredBaseUrl.TrimEnd('/') + "/", UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }

        return new Uri($"https://{Subdomain.Trim()}.chargify.com/", UriKind.Absolute);
    }

    public void ValidateForApiUse()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("Maxio:ApiKey is required.");

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is required.");

        _ = GetBaseAddress();
    }
}
