using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;

    public Uri GetBaseAddress()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
            throw new InvalidOperationException("Maxio:BaseUrl is not configured.");

        var baseUrl = BaseUrl.EndsWith("/", StringComparison.Ordinal) ? BaseUrl : $"{BaseUrl}/";
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var address) ||
            address.Scheme is not ("https" or "http"))
        {
            throw new InvalidOperationException("Maxio:BaseUrl must be an absolute HTTP(S) URL.");
        }

        return address;
    }
}
