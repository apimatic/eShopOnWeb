namespace Microsoft.eShopWeb.Infrastructure.Configuration;

// Bound from the "Maxio" configuration section (user-secrets / appsettings / environment
// variables) — mirrors how CatalogSettings is bound. Only ApiKey is sensitive; everything
// else is environment metadata. See MaxioBillingClient for how BaseUrl/Subdomain/Environment
// resolve into the outbound server the typed HttpClient targets.
public class MaxioSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;

    // Maxio data-center region ("US" or "EU") — NOT the deployment target. See BaseUrl.
    public string Environment { get; set; } = "US";

    // Optional explicit override for the outbound base URL. When set, it wins over the
    // Subdomain-derived host, so the same build can be pointed at production, a dev/sandbox
    // tenant, or a local mock server purely through configuration. Leave empty to derive the
    // host from Subdomain + Environment.
    public string? BaseUrl { get; set; }

    public string ProductFamilyHandle { get; set; } = string.Empty;
    public int ProductFamilyId { get; set; }

    public string DefaultProductHandle { get; set; } = string.Empty;
    public int DefaultProductId { get; set; }

    public string AlternateProductHandle { get; set; } = string.Empty;
    public int AlternateProductId { get; set; }

    public string MeteredComponentHandle { get; set; } = string.Empty;
    public int MeteredComponentId { get; set; }
}
