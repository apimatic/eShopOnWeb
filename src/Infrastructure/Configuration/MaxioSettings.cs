using System;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed options for the Maxio Advanced Billing integration (mirror <c>CatalogSettings</c>). Bound
/// from configuration/user-secrets under the "Maxio" section - never hardcode these values.
/// </summary>
public class MaxioSettings
{
    /// <summary>The Maxio API key. Sensitive - must only ever come from user-secrets/environment.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The site subdomain, used to derive the default host when <see cref="BaseUrl"/> is not set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>The Maxio data-center region ("US" or "EU") - NOT the deployment target.</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Optional explicit override for the outbound base URL. When set, it wins verbatim over the
    /// <see cref="Subdomain"/>-derived host - this is the single knob that lets the same build target
    /// production, a dev/sandbox tenant, or a local mock server purely through configuration.
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

    public bool IsEuRegion => string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the explicit base-URL override, or null when none is configured (meaning the
    /// subdomain-derived default should be used instead). An explicit override always wins - see
    /// plan.md §2.3.
    /// </summary>
    public string? ResolveBaseUrl() => string.IsNullOrWhiteSpace(BaseUrl) ? null : BaseUrl;
}
