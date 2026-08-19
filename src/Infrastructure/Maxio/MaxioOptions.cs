using System;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }
    public string? BaseUrl { get; set; }

    public Uri ResolveBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            var trimmed = BaseUrl.Trim();
            if (!trimmed.EndsWith('/'))
            {
                trimmed += "/";
            }

            return new Uri(trimmed, UriKind.Absolute);
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio:Subdomain is required when Maxio:BaseUrl is not set.");
        }

        return new Uri($"https://{Subdomain.Trim()}.chargify.com/", UriKind.Absolute);
    }
}
