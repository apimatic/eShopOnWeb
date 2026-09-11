namespace Microsoft.eShopWeb.Infrastructure.Maxio;

public class MaxioSettings
{
    public string ApiKey { get; set; } = null!;
    public string Subdomain { get; set; } = null!;
    public string ProductFamilyHandle { get; set; } = null!;
    public string? BaseUrl { get; set; }
}
