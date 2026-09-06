using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; init; } = string.Empty;
    public string Subdomain { get; init; } = string.Empty;
    public string ProductFamilyHandle { get; init; } = string.Empty;
    public string? BaseUrl { get; init; }

    public string GetBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl;
        }

        return $"https://{Subdomain}.chargify.com/";
    }

    public static bool IsValid(MaxioOptions options)
    {
        return !string.IsNullOrWhiteSpace(options.ApiKey)
            && !string.IsNullOrWhiteSpace(options.Subdomain)
            && !string.IsNullOrWhiteSpace(options.ProductFamilyHandle)
            && Uri.TryCreate(options.GetBaseUrl(), UriKind.Absolute, out var baseUri)
            && baseUri.Scheme == Uri.UriSchemeHttps;
    }
}
