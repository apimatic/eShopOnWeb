namespace Microsoft.eShopWeb.ApplicationCore;

public class MaxioSettings
{
    public string? ApiKey { get; set; }
    public string Subdomain { get; set; } = "";
    public string? BaseUrl { get; set; }
    public string ProductFamilyHandle { get; set; } = "";
}
