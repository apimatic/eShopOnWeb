using System;
using System.Text.RegularExpressions;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public static bool IsValid(MaxioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey) ||
            string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            return Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) &&
                   string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        return !string.IsNullOrWhiteSpace(options.Subdomain) &&
               Regex.IsMatch(options.Subdomain, "^[A-Za-z0-9-]+$");
    }
}
