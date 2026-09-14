using System;
using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl() => !string.IsNullOrWhiteSpace(BaseUrl)
        ? BaseUrl
        : $"https://{Subdomain}.chargify.com";

    public static bool IsValid(MaxioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey) ||
            string.IsNullOrWhiteSpace(options.Subdomain) ||
            string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            return false;
        }

        if (!Regex.IsMatch(options.Subdomain, "^[a-zA-Z0-9][a-zA-Z0-9-]*$"))
        {
            return false;
        }

        if (!Uri.TryCreate(options.ResolveBaseUrl(), UriKind.Absolute, out var baseUri))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(baseUri.UserInfo) ||
            !string.IsNullOrEmpty(baseUri.Query) ||
            !string.IsNullOrEmpty(baseUri.Fragment))
        {
            return false;
        }

        return baseUri.Scheme == Uri.UriSchemeHttps ||
               (baseUri.Scheme == Uri.UriSchemeHttp && baseUri.IsLoopback);
    }
}
