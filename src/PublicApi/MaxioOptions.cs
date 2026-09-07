namespace Microsoft.eShopWeb.PublicApi;

public class MaxioOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string? Environment { get; set; }
    public string? BaseUrl { get; set; }
    public string? ProductFamilyHandle { get; set; }
}
