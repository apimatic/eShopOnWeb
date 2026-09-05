namespace Microsoft.eShopWeb.PublicApi;

public class MaxioConfiguration
{
    public const string ConfigName = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }
    public string? BaseUrl { get; set; }
}
