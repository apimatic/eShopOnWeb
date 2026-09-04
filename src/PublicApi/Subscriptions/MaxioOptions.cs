using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

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
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var configuredBaseUrl))
                throw new MaxioConfigurationException("Maxio:BaseUrl must be an absolute URI.");

            return configuredBaseUrl;
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
            throw new MaxioConfigurationException("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");

        return new Uri($"https://{Subdomain}.chargify.com/", UriKind.Absolute);
    }
}

public sealed class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}
