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
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var configuredUri))
            {
                throw new SubscriptionConfigurationException("Maxio:BaseUrl must be an absolute URI.");
            }

            return configuredUri;
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new SubscriptionConfigurationException("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }

        return new Uri($"https://{Subdomain}.chargify.com", UriKind.Absolute);
    }
}
