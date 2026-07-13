using System;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

// Typed options for the Maxio Advanced Billing integration (mirrors CatalogSettings usage,
// bound via services.Configure<MaxioSettings>(configuration.GetSection("Maxio"))).
// Values come from appsettings / user-secrets / environment variables — never hardcoded.
public class MaxioSettings : ISubscriptionCatalogOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;

    // Maxio data-center region (US/EU) — NOT the deployment target. See BaseUrl.
    public string Environment { get; set; } = "US";

    // Explicit outbound target-server override. When set, it wins verbatim over the
    // Subdomain-derived host, so the same build can hit production, a dev/sandbox tenant,
    // or a local mock server purely through configuration (plan §2.3).
    public string? BaseUrl { get; set; }

    public string ProductFamilyHandle { get; set; } = string.Empty;
    public int ProductFamilyId { get; set; }

    public string DefaultProductHandle { get; set; } = string.Empty;
    public int DefaultProductId { get; set; }

    public string AlternateProductHandle { get; set; } = string.Empty;
    public int AlternateProductId { get; set; }

    public string MeteredComponentHandle { get; set; } = string.Empty;
    public int MeteredComponentId { get; set; }

    // Resolution order: explicit BaseUrl wins; otherwise derive the host from Subdomain + region.
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? $"https://{Subdomain}.ebilling.maxio.com"
            : $"https://{Subdomain}.chargify.com";
    }
}
