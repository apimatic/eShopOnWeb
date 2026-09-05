using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; init; } = string.Empty;
    public string Subdomain { get; init; } = string.Empty;
    public string ProductFamilyHandle { get; init; } = string.Empty;
    public string? BaseUrl { get; init; }

    public bool IsValid(out string? error)
    {
        if (string.IsNullOrWhiteSpace(ApiKey) || string.IsNullOrWhiteSpace(Subdomain) ||
            string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            error = "ApiKey, Subdomain, and ProductFamilyHandle are required.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl) &&
            (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps))
        {
            error = "BaseUrl must be an absolute HTTPS URL when specified.";
            return false;
        }

        error = null;
        return true;
    }

    public Uri GetBaseUri()
    {
        var baseUrl = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com/"
            : BaseUrl!;

        return new Uri(baseUrl.TrimEnd('/') + "/", UriKind.Absolute);
    }
}
