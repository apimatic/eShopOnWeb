namespace Microsoft.eShopWeb.ApplicationCore;

public class MaxioSettings
{
    public string ApiKey { get; set; } = null!;
    public string Subdomain { get; set; } = null!;
    public string? BaseUrl { get; set; }
    public string ProductFamilyHandle { get; set; } = null!;

    public string GetBaseUrl()
    {
        if (!string.IsNullOrEmpty(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }
        return $"https://{Subdomain}.chargify.com";
    }
}
