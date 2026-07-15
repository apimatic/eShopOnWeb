using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed configuration for the Maxio Advanced Billing integration (mirrors <c>CatalogSettings</c>'
/// role as a typed-options class; see §2.3/§5 of the integration plan). Bound from the "Maxio"
/// configuration section; <see cref="ApiKey"/> is the only value that must live in user-secrets.
/// </summary>
public class MaxioSettings
{
    /// <summary>Maxio API key. Sensitive — user-secrets/environment variable only, never appsettings.json.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, e.g. "apimatic-hackathon" in "apimatic-hackathon.chargify.com".</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Maxio data-center region: "US" or "EU". Not the deployment target — see <see cref="BaseUrl"/>.</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Optional explicit override for the outbound base URL. When set, it wins over the
    /// <see cref="Subdomain"/>-derived host, so the same build can target production, a dev/sandbox
    /// tenant, or a local mock server purely through configuration (§2.3 — hard requirement).
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
    /// The explicit base-URL override, or null when absent — callers must then derive the host
    /// from <see cref="Subdomain"/> (+ region). Resolution order per §2.3: explicit override always wins.
    /// </summary>
    public string? ResolveBaseUrl() => string.IsNullOrWhiteSpace(BaseUrl) ? null : BaseUrl;

    /// <summary>
    /// Configures the Maxio SDK client options from this settings instance — the single place that
    /// resolves the outbound target server: an explicit <see cref="BaseUrl"/> always wins; otherwise
    /// the host is derived from <see cref="Subdomain"/> for the configured region (§2.3/§4.3).
    /// </summary>
    public void ConfigureClientOptions(MaxioAdvancedBillingClientOptions options)
    {
        options.BasicAuth = new BasicAuthCredentials { Username = ApiKey, Password = "x" };
        options.Environment = IsEuRegion ? ServerEnvironment.Eu : ServerEnvironment.Us;

        var explicitBaseUrl = ResolveBaseUrl();
        if (IsEuRegion)
        {
            if (explicitBaseUrl is not null)
            {
                options.Server.Production.Eu.BaseUrl = explicitBaseUrl;
            }
            else
            {
                options.Server.Production.Eu.Site = Subdomain;
            }
        }
        else
        {
            if (explicitBaseUrl is not null)
            {
                options.Server.Production.Us.BaseUrl = explicitBaseUrl;
            }
            else
            {
                options.Server.Production.Us.Site = Subdomain;
            }
        }
    }
}
