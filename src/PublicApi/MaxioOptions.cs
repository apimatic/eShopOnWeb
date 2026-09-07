namespace Microsoft.eShopWeb.PublicApi;

public class MaxioOptions
{
    public required string ApiKey { get; set; }
    public required string Subdomain { get; set; }
    public required string ProductFamilyHandle { get; set; }
    public string? BaseUrl { get; set; }
}
