namespace Microsoft.eShopWeb.PublicApi.Maxio;

public record MaxioOptions
{
    public string Subdomain { get; set; } = "cp-exp-1";
    public string ProductFamilyHandle { get; set; } = "eshop-subscribe";
    public string? BaseUrl { get; set; }
}
