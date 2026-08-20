using System;

namespace Microsoft.eShopWeb.PublicApi.SubscriptionBilling;

public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public string ApiBaseUrl => string.IsNullOrWhiteSpace(BaseUrl)
        ? $"https://{Subdomain}.chargify.com"
        : BaseUrl;

    public static bool IsValidBaseUrl(string? value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }
}
