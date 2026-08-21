using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
    public string? Environment { get; set; }
    public string DefaultProductHandle { get; set; } = "eshop-pro";
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan CallBudget { get; set; } = TimeSpan.FromSeconds(30);
}
