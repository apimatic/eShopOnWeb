using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;

    public string Subdomain { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;

    public string? BaseUrl { get; set; }

    public Uri GetApiBaseAddress()
    {
        var configuredBaseUrl = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com"
            : BaseUrl;

        if (!Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var baseAddress) ||
            baseAddress.Scheme is not ("https" or "http"))
        {
            throw new InvalidOperationException("Maxio:BaseUrl must be an absolute HTTP(S) URL, or Maxio:Subdomain must be configured.");
        }

        // HttpClient's relative URI resolution requires a trailing slash. The configured
        // host/path itself is preserved, including when BaseUrl points at a test server.
        return new Uri(baseAddress.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
    }
}
