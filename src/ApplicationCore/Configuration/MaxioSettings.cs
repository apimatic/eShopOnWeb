namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public required string ApiKey { get; set; }
    public required string Subdomain { get; set; }
    public required string ProductFamilyHandle { get; set; }
    public string? BaseUrl { get; set; }
    public string Environment { get; set; } = "sandbox";

    public string GetBaseUrl()
    {
        if (!string.IsNullOrEmpty(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return $"https://{Subdomain}.chargify.com";
    }
}
