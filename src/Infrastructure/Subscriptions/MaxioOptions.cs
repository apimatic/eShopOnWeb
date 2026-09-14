using System;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string Environment { get; set; } = "US";
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !string.IsNullOrWhiteSpace(Subdomain) &&
        !string.IsNullOrWhiteSpace(ProductFamilyHandle);
}
