using System;

namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }
    public string? BaseUrl { get; set; }

    public Uri GetBaseAddress()
    {
        var value = string.IsNullOrWhiteSpace(BaseUrl)
            ? $"https://{Subdomain}.chargify.com/"
            : BaseUrl;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var address) ||
            (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("Maxio:BaseUrl must be an absolute HTTP(S) URL, or Maxio:Subdomain must be configured.");
        }

        return new Uri(address.ToString().TrimEnd('/') + "/", UriKind.Absolute);
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
            throw new InvalidOperationException("Maxio:ApiKey is required.");
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is required.");
        if (string.IsNullOrWhiteSpace(Subdomain) && string.IsNullOrWhiteSpace(BaseUrl))
            throw new InvalidOperationException("Maxio:Subdomain is required when Maxio:BaseUrl is not set.");

        _ = GetBaseAddress();
    }
}
