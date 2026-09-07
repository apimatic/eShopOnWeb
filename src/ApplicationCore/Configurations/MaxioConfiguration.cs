namespace Microsoft.eShopWeb.ApplicationCore.Configurations;

public class MaxioConfiguration
{
    public const string CONFIG_NAME = "Maxio";

    public string ApiKey { get; set; } = null!;
    public string Subdomain { get; set; } = null!;
    public string? BaseUrl { get; set; }
    public string ProductFamilyHandle { get; set; } = null!;
}
