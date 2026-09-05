using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; init; } = string.Empty;
    public string Subdomain { get; init; } = string.Empty;
    public string ProductFamilyHandle { get; init; } = string.Empty;
    public string? BaseUrl { get; init; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey)
        && !string.IsNullOrWhiteSpace(Subdomain)
        && !string.IsNullOrWhiteSpace(ProductFamilyHandle);

    public Uri GetApiBaseUri()
    {
        var baseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? $"https://{Subdomain}.chargify.com/" : BaseUrl;
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Maxio:BaseUrl must be an absolute HTTPS URL.");
        return uri.ToString().EndsWith("/", StringComparison.Ordinal) ? uri : new Uri(uri + "/");
    }

    public void Validate()
    {
        if (!IsConfigured)
            throw new InvalidOperationException("Maxio configuration requires ApiKey, Subdomain, and ProductFamilyHandle.");
        _ = GetApiBaseUri();
    }
}
