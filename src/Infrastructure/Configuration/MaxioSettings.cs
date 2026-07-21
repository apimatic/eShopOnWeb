using System;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed options for the Maxio Advanced Billing integration (mirrors <c>CatalogSettings</c>'s
/// usage). Bound from the "Maxio" configuration section, backed by .NET user-secrets for the
/// API key — never committed to source.
/// </summary>
public class MaxioSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Maxio data-center region ("US" or "EU") — not the deployment target (§2.3).</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Optional explicit override for the outbound base URL. When set, it wins verbatim over the
    /// subdomain-derived host, so the identical build can target production, a dev/sandbox
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

    /// <summary>
    /// Resolution order the client must honor (§2.3): an explicit <see cref="BaseUrl"/> wins
    /// verbatim; only when it is absent is the host derived from <see cref="Subdomain"/> + region.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl;
        }

        return string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase)
            ? $"https://{Subdomain}.ebilling.maxio.com"
            : $"https://{Subdomain}.chargify.com";
    }
}
