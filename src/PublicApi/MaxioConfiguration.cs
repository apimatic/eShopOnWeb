namespace Microsoft.eShopWeb.PublicApi;

public class MaxioConfiguration
{
    public const string CONFIG_NAME = "Maxio";
    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }
    public string? BaseUrl { get; set; }

    public string GetBaseUrl() =>
        !string.IsNullOrEmpty(BaseUrl)
            ? BaseUrl
            : $"https://{Subdomain}.chargify.com";
}
