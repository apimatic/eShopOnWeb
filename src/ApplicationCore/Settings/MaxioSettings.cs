namespace Microsoft.eShopWeb.ApplicationCore.Settings;

public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public required string ApiKey { get; set; }
    public required string Subdomain { get; set; }
    public string? BaseUrl { get; set; }
    public required string ProductFamilyHandle { get; set; }

    public string GetBaseUrl()
    {
        if (!string.IsNullOrEmpty(BaseUrl))
            return BaseUrl;
        return $"https://{Subdomain}.maxio.com/api/v1";
    }
}
