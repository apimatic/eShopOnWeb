namespace Microsoft.eShopWeb.ApplicationCore;

public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string ApiKey { get; set; } = "";
    public string Subdomain { get; set; } = "";
    public string BaseUrl { get; set; } = "";
    public string ProductFamilyHandle { get; set; } = "";
}
