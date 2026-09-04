using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;

    public string Subdomain { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;

    public string? BaseUrl { get; set; }

    public Uri GetBaseUri()
    {
        var configuredBaseUrl = BaseUrl?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredBaseUrl))
        {
            if (!Uri.TryCreate(configuredBaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var overrideUri))
            {
                throw new MaxioConfigurationException("Maxio:BaseUrl must be an absolute URI.");
            }

            return overrideUri;
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException("Maxio:Subdomain is not configured.");
        }

        return new Uri($"https://{Subdomain.Trim()}.chargify.com/", UriKind.Absolute);
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new MaxioConfigurationException("Maxio:ApiKey is not configured.");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            throw new MaxioConfigurationException("Maxio:ProductFamilyHandle is not configured.");

        _ = GetBaseUri();
    }
}

public sealed class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}
