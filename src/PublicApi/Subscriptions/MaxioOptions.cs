using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;

    public Uri GetBaseUri()
    {
        var value = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com/"
            : BaseUrl;

        if (!value.EndsWith('/'))
        {
            value += "/";
        }

        return new Uri(value, UriKind.Absolute);
    }
}
