namespace Microsoft.eShopWeb.PublicApi.MaxioIntegration;

public class MaxioSettings
{
    public const string ConfigurationSectionName = "Maxio";
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
}
