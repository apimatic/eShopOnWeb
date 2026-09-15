using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; init; } = string.Empty;
    public string Subdomain { get; init; } = string.Empty;
    public string ProductFamilyHandle { get; init; } = string.Empty;
    public string? BaseUrl { get; init; }

    public Uri GetBaseUri()
    {
        var baseUrl = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com/"
            : BaseUrl;

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException("Maxio:BaseUrl must be an absolute HTTPS URL.");
        }

        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
    }

    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(ApiKey) || string.IsNullOrWhiteSpace(Subdomain) ||
            string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio integration is not configured. Set Maxio:ApiKey, Maxio:Subdomain, and Maxio:ProductFamilyHandle.");
        }
    }
}
