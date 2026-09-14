using System;
using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;

    public string ResolveBaseUrl() => !string.IsNullOrWhiteSpace(BaseUrl)
        ? BaseUrl
        : $"https://{Subdomain}.chargify.com";

    public static bool IsValid(MaxioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey) ||
            string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl) &&
            (string.IsNullOrWhiteSpace(options.Subdomain) ||
             !Regex.IsMatch(options.Subdomain, "^[A-Za-z0-9-]+$")))
        {
            return false;
        }

        return Uri.TryCreate(options.ResolveBaseUrl(), UriKind.Absolute, out var uri) &&
               uri.Scheme == Uri.UriSchemeHttps;
    }
}
