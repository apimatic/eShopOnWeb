namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioSettings
{
    public string ApiKey { get; set; } = null!;
    public string Subdomain { get; set; } = null!;
    public string ProductFamilyHandle { get; set; } = null!;
    public string? BaseUrl { get; set; }

    public string GetBaseUrl()
    {
        if (!string.IsNullOrEmpty(BaseUrl))
            return BaseUrl;

        return $"https://{Subdomain}.chargify.com";
    }
}
