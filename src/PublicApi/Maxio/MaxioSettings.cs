namespace Microsoft.eShopWeb.PublicApi.Maxio;

public class MaxioSettings
{
    public const string ConfigSectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
            return BaseUrl.TrimEnd('/');

        return $"https://{Subdomain}.chargify.com";
    }
}
