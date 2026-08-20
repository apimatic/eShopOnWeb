using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public static bool IsValid(MaxioOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey) ||
            string.IsNullOrWhiteSpace(options.Subdomain) ||
            string.IsNullOrWhiteSpace(options.ProductFamilyHandle))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(options.BaseUrl) ||
               Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
    }
}
