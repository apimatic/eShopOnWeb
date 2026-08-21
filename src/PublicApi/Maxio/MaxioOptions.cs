using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

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
            ? $"https://{Require(Subdomain, nameof(Subdomain))}.chargify.com"
            : BaseUrl;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("Maxio:BaseUrl must be an absolute HTTP or HTTPS URL.");
        }

        return uri;
    }

    public void Validate()
    {
        Require(ApiKey, nameof(ApiKey));
        Require(ProductFamilyHandle, nameof(ProductFamilyHandle));
        _ = GetBaseUri();
    }

    private static string Require(string? value, string key)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Maxio:{key} is required.");
        }

        return value;
    }
}
