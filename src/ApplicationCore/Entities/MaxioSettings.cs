namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public class MaxioSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public string GetApiBaseUrl()
    {
        if (!string.IsNullOrEmpty(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }
        return $"https://{Subdomain}.maxio.com/api/v1";
    }
}
