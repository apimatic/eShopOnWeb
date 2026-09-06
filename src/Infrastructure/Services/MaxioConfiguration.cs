namespace Microsoft.eShopWeb.Infrastructure.Services;

public class MaxioConfiguration
{
    public const string ConfigSection = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public string ProductFamilyHandle { get; set; } = string.Empty;
}
