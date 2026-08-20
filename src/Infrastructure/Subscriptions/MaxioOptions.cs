using System;

namespace Microsoft.eShopWeb.Infrastructure.Subscriptions;

public sealed class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("Configuration key Maxio:ApiKey is required.");
        }
        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException("Configuration key Maxio:Subdomain is required.");
        }
        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException("Configuration key Maxio:ProductFamilyHandle is required.");
        }
    }
}
