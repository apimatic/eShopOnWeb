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
                throw new InvalidOperationException("Maxio:BaseUrl must be an absolute URI.");

            return configuredUri;
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
            throw new InvalidOperationException("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");

        // This is the production server template in maxio-spec/openapi.yaml.
        return new Uri($"https://{Subdomain}.chargify.com/", UriKind.Absolute);
    }
}
