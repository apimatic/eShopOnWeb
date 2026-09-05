using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

/// <summary>Configuration for the Maxio Advanced Billing site used by this API.</summary>
public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; init; }
    public string? Subdomain { get; init; }
    public string? ProductFamilyHandle { get; init; }
    public string? BaseUrl { get; init; }

    public Uri GetBaseUri()
    {
        var baseUrl = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Require(Subdomain, nameof(Subdomain))}.chargify.com/"
            : BaseUrl;

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new MaxioConfigurationException("Maxio:BaseUrl must be an absolute HTTPS URL.");

        return new Uri(uri.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
    }

    public string GetApiKey() => Require(ApiKey, "ApiKey");
    public string GetProductFamilyHandle() => Require(ProductFamilyHandle, "ProductFamilyHandle");

    private static string Require(string? value, string setting)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new MaxioConfigurationException($"Maxio:{setting} is not configured.");

        return value;
    }
}
