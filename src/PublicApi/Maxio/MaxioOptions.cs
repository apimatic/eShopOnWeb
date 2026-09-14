using System;

namespace Microsoft.eShopWeb.PublicApi.Maxio;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;

    public string Subdomain { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;

    public string? BaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(Subdomain);

    public Uri GetBaseAddress()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return new Uri(BaseUrl.EndsWith('/') ? BaseUrl : BaseUrl + "/");
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio integration is not configured. Set Maxio:Subdomain (from MAXIO_SITE_SUBDOMAIN) or Maxio:BaseUrl.");
        }

        return new Uri($"https://{Subdomain}.chargify.com/");
    }
}
