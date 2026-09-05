using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Configuration for the Maxio Advanced Billing site that owns subscription state.</summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; init; }
    public string? Subdomain { get; init; }
    public string? ProductFamilyHandle { get; init; }
    public string? BaseUrl { get; init; }

    public Uri GetBaseAddress()
    {
        if (string.IsNullOrWhiteSpace(ApiKey) || string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new MaxioConfigurationException("Maxio is not configured. Set Maxio:ApiKey and Maxio:ProductFamilyHandle.");
        }

        var value = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{RequireSubdomain()}.chargify.com"
            : BaseUrl;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new MaxioConfigurationException("Maxio:BaseUrl must be an absolute HTTPS URL.");
        }

        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
    }

    private string RequireSubdomain()
    {
        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }

        return Subdomain;
    }
}

public sealed class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message) { }
}
