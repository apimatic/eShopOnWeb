using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;

    public Uri GetApiBaseUri()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (!Uri.TryCreate(BaseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var overrideUri))
            {
                throw new MaxioConfigurationException("Maxio:BaseUrl must be an absolute URI.");
            }

            return overrideUri;
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }

        return new Uri($"https://{Subdomain.Trim()}.chargify.com/", UriKind.Absolute);
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new MaxioConfigurationException("Maxio:ApiKey is required.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new MaxioConfigurationException("Maxio:ProductFamilyHandle is required.");
        }

        _ = GetApiBaseUri();
    }
}

public sealed class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}
