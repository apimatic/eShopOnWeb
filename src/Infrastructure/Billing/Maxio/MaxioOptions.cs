using System;
using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;

    public string GetApiBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl;
        }

        return $"https://{Subdomain}.chargify.com";
    }

    public static bool IsValid(MaxioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey) ||
            string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return Regex.IsMatch(options.Subdomain, "^[a-zA-Z0-9][a-zA-Z0-9-]*$");
        }

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps ||
               (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    }
}
