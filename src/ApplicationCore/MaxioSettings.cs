namespace Microsoft.eShopWeb;

public class MaxioSettings
{
    public string ApiKey { get; set; } = null!;
    public string SiteSubdomain { get; set; } = null!;
    public string? BaseUrl { get; set; }
    public string ProductFamilyHandle { get; set; } = "eshop-subscribe";
    public bool SandboxMode { get; set; } = true;
}
