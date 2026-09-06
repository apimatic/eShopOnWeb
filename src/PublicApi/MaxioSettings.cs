namespace Microsoft.eShopWeb.PublicApi;

public class MaxioSettings
{
    public string ApiKey { get; set; } = null!;
    public string Subdomain { get; set; } = null!;
    public string? BaseUrl { get; set; }
    public string ProductFamilyHandle { get; set; } = null!;

    public string GetBaseUrl() => string.IsNullOrEmpty(BaseUrl)
        ? $"https://{Subdomain}.chargify.com"
        : BaseUrl;
}
