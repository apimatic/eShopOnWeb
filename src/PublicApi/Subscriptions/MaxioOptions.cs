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
        var value = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com/"
            : BaseUrl;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new MaxioConfigurationException("Maxio:BaseUrl must be an absolute HTTP or HTTPS URL.");
        }

        return new Uri(uri.ToString().EndsWith("/", StringComparison.Ordinal) ? uri.ToString() : uri + "/");
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new MaxioConfigurationException("Maxio:ApiKey is not configured.");

        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
            throw new MaxioConfigurationException("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            throw new MaxioConfigurationException("Maxio:ProductFamilyHandle is not configured.");
    }
}

public sealed class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message)
    {
    }
}
