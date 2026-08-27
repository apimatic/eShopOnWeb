namespace Microsoft.eShopWeb.PublicApi.Subscriptions;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(ApiKey) || string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new MaxioConfigurationException();
        }

        if (string.IsNullOrWhiteSpace(BaseUrl) && string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new MaxioConfigurationException();
        }

        if (!string.IsNullOrWhiteSpace(BaseUrl) &&
            (!System.Uri.TryCreate(BaseUrl, System.UriKind.Absolute, out var uri) ||
             (uri.Scheme != System.Uri.UriSchemeHttps && uri.Scheme != System.Uri.UriSchemeHttp)))
        {
            throw new MaxioConfigurationException();
        }
    }
}
