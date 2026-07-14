namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed configuration for the Maxio Advanced Billing integration (mirrors <c>CatalogSettings</c>).
/// Bound from the "Maxio" configuration section; the API key is supplied through .NET user-secrets
/// (§2.3) and never appears in source or appsettings.json.
/// </summary>
public class MaxioSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Maxio data-center region ("US" or "EU") — a separate axis from the deployment target below.</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Explicit outbound base-URL override. When set, it wins verbatim over the subdomain-derived
    /// host — the single knob that lets the identical build target production, a dev/sandbox
    /// tenant, or a local mock server purely through configuration (§2.3).
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ProductFamilyHandle { get; set; } = string.Empty;
    public int ProductFamilyId { get; set; }

    public string DefaultProductHandle { get; set; } = string.Empty;
    public int DefaultProductId { get; set; }

    public string AlternateProductHandle { get; set; } = string.Empty;
    public int AlternateProductId { get; set; }

    public string MeteredComponentHandle { get; set; } = string.Empty;
    public int MeteredComponentId { get; set; }

    public bool IsEuEnvironment => string.Equals(Environment, "EU", System.StringComparison.OrdinalIgnoreCase);
}
