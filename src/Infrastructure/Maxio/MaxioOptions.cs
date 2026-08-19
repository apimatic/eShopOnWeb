namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioOptions
{
    public const string SectionName = "Maxio";
    public const string ApiKeyKey = "Maxio:ApiKey";
    public const string SubdomainKey = "Maxio:Subdomain";
    public const string ProductFamilyHandleKey = "Maxio:ProductFamilyHandle";
    public const string BaseUrlKey = "Maxio:BaseUrl";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/') + "/";
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            return string.Empty;
        }

        return $"https://{Subdomain}.chargify.com/";
    }
}
