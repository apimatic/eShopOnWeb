using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; init; } = string.Empty;
    public string Subdomain { get; init; } = string.Empty;
    public string ProductFamilyHandle { get; init; } = string.Empty;
    public string? BaseUrl { get; init; }

    public Uri GetApiBaseUri()
    {
        var configuredBaseUrl = string.IsNullOrWhiteSpace(BaseUrl)
            ? (string.IsNullOrWhiteSpace(Subdomain) ? null : $"https://{Subdomain}.chargify.com")
            : BaseUrl;

        if (string.IsNullOrWhiteSpace(ApiKey) || string.IsNullOrWhiteSpace(ProductFamilyHandle) ||
            !Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttps && baseUri.Scheme != Uri.UriSchemeHttp))
        {
            throw new MaxioConfigurationException("Maxio configuration is incomplete. Configure Maxio:ApiKey, Maxio:Subdomain, and Maxio:ProductFamilyHandle (or Maxio:BaseUrl).");
        }

        return new Uri(baseUri.ToString().TrimEnd('/') + "/", UriKind.Absolute);
    }
}

public sealed class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message) { }
}
