namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed configuration for the Maxio Advanced Billing integration (bound from the "Maxio" configuration
/// section - user-secrets in development; never committed to source). See plan.md §2.3/§5.
/// </summary>
public class MaxioSettings
{
    /// <summary>The Maxio/Chargify API key. Sent as the HTTP Basic username; never logged or persisted.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The site subdomain used to derive the host when <see cref="BaseUrl"/> is not set.</summary>
    public string? Subdomain { get; set; }

    /// <summary>The Maxio data-center region ("US" or "EU") - a separate axis from the deployment target below.</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Optional explicit override for the outbound base URL. When set, it wins verbatim over the
    /// subdomain-derived host, so the identical build can target production, a dev/sandbox tenant, or a
    /// local mock server purely through configuration (plan.md §2.3). Leave empty to derive from
    /// <see cref="Subdomain"/> + <see cref="Environment"/>.
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
}
