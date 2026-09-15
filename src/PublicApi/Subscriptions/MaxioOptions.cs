using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string Environment { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;

    public Uri GetBaseUri()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var configuredUri))
            {
                throw new MaxioConfigurationException("Maxio:BaseUrl must be an absolute URI.");
            }

            return configuredUri;
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException("Maxio:Subdomain is not configured.");
        }

        // Maxio's US-hosted API uses chargify.com for both test and production sites.
        // EU-hosted accounts use ebilling.maxio.com. The explicit BaseUrl setting is
        // available for sites hosted in any other supported environment.
        var host = Environment.Contains("eu", StringComparison.OrdinalIgnoreCase)
            ? $"{Subdomain}.ebilling.maxio.com"
            : $"{Subdomain}.chargify.com";

        return new Uri($"https://{host}/", UriKind.Absolute);
    }
}

public sealed class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}
