namespace Microsoft.eShopWeb.PublicApi.SubscriptionEndpoints;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";
    public const string HttpClientName = "MaxioAdvancedBilling";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
}
