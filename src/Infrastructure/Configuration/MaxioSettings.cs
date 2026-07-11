namespace Microsoft.eShopWeb.Infrastructure.Configuration;

public class MaxioSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string Environment { get; set; } = "US";
    public string BaseUrl { get; set; } = string.Empty;

    public int ProductFamilyId { get; set; }
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public int DefaultProductId { get; set; }
    public string DefaultProductHandle { get; set; } = string.Empty;
    public int AlternateProductId { get; set; }
    public string AlternateProductHandle { get; set; } = string.Empty;
    public int MeteredComponentId { get; set; }
    public string MeteredComponentHandle { get; set; } = string.Empty;

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrEmpty(BaseUrl))
        {
            return BaseUrl;
        }

        var region = Environment?.ToUpperInvariant() == "EU" ? "ebilling.maxio" : "chargify";
        return $"https://{Subdomain}.{region}.com";
    }
}
