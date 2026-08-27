using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(ApiKey) ||
            string.IsNullOrWhiteSpace(Subdomain) ||
            string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(BaseUrl) ||
               (Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps);
    }
}
