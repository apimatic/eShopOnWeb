using System;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;

    public string? TryResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            return null;
        }

        // US (default, including sandbox sites): https://{site}.chargify.com
        // EU hosting: https://{site}.ebilling.maxio.com
        // Confirmed from Maxio Advanced Billing SDK environment map.
        var environment = Environment.GetEnvironmentVariable("MAXIO_ENVIRONMENT");
        if (string.Equals(environment, "EU", StringComparison.OrdinalIgnoreCase))
        {
            return $"https://{Subdomain}.ebilling.maxio.com";
        }

        return $"https://{Subdomain}.chargify.com";
    }

    public void EnsureConfigured()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            throw new InvalidOperationException("Maxio:ApiKey is not configured.");
        }

        if (string.IsNullOrWhiteSpace(ProductFamilyHandle))
        {
            throw new InvalidOperationException("Maxio:ProductFamilyHandle is not configured.");
        }

        if (TryResolveBaseUrl() is null)
        {
            throw new InvalidOperationException("Set Maxio:BaseUrl or Maxio:Subdomain so the Advanced Billing API address can be resolved.");
        }
    }
}
