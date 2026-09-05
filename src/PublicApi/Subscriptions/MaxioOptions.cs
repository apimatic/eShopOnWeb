using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Settings for Maxio Advanced Billing. Values are supplied through user-secrets or environment configuration.</summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public Uri GetBaseUri()
    {
        var baseUrl = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com"
            : BaseUrl;

        if (string.IsNullOrWhiteSpace(ApiKey) || string.IsNullOrWhiteSpace(ProductFamilyHandle) ||
            (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain)))
        {
            throw new MaxioConfigurationException(
                "Maxio is not configured. Set Maxio:ApiKey, Maxio:ProductFamilyHandle, and either Maxio:Subdomain or Maxio:BaseUrl.");
        }

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new MaxioConfigurationException("Maxio:BaseUrl must be an absolute HTTP(S) URL when supplied.");
        }

        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
    }
}

public sealed class MaxioConfigurationException : Exception
{
    public MaxioConfigurationException(string message) : base(message) { }
}
