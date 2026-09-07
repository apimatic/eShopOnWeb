namespace Microsoft.eShopWeb.PublicApi;

public class MaxioSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string Environment { get; set; } = "sandbox";
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public string GetBaseUrl()
    {
        if (!string.IsNullOrEmpty(BaseUrl))
            return BaseUrl;

        return Environment.ToLower() == "sandbox"
            ? $"https://{Subdomain}.chargify.com"
            : $"https://{Subdomain}.ebilling.maxio.com";
    }
}
