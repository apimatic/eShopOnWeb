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
            if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var configuredUri))
            {
                throw new InvalidOperationException("Maxio:BaseUrl must be an absolute URL.");
            }

            return configuredUri;
        }

        if (!Uri.TryCreate($"https://{Subdomain}.chargify.com", UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException("Maxio:Subdomain must be configured when Maxio:BaseUrl is not set.");
        }

        // HttpClient resolves relative request paths against the final path segment.
        // Preserve the configured address while ensuring it behaves as an API base path.
        return new Uri(uri.ToString().TrimEnd('/') + "/", UriKind.Absolute);
    }
}
