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
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var configuredBaseUrl))
                throw new MaxioConfigurationException("Maxio:BaseUrl must be an absolute URL.");

            return configuredBaseUrl;
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
            throw new MaxioConfigurationException("Maxio:Subdomain is not configured.");

        // The local Maxio specification defines the default US production API server as
        // https://{site}.chargify.com. Maxio sandbox sites use this same API host shape.
        return new Uri($"https://{Subdomain}.chargify.com/", UriKind.Absolute);
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
