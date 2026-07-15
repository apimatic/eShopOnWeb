using System;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed configuration for the Maxio Advanced Billing integration (mirrors the <c>CatalogSettings</c>
/// pattern). Bound from the "Maxio" configuration section (user-secrets / appsettings / environment
/// variables) — never hardcoded (plan.md §2.3).
/// </summary>
public class MaxioSettings
{
    /// <summary>The Maxio API key. Sensitive — must only ever come from user-secrets/environment.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, used to derive the host when <see cref="BaseUrl"/> is not set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>The Maxio data-center region ("US" or "EU") — NOT the deployment target (plan.md §2.3).</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Optional explicit override for the outbound base URL. When set, it wins verbatim over the
    /// subdomain-derived host — the single knob that lets the identical build target production, a
    /// dev/sandbox tenant, or a local mock server (plan.md §2.3).
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

    /// <summary>True when the configured region is the EU data center.</summary>
    public bool IsEuRegion => string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolves the outbound base URL: the explicit <see cref="BaseUrl"/> override if present, otherwise
    /// the host derived from <see cref="Subdomain"/> + <see cref="Environment"/>. This is the single place
    /// that decision is made (plan.md §2.3/§4.3) — callers must never fall back to a hardcoded host.
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.TrimEnd('/');
        }

        return IsEuRegion
            ? $"https://{Subdomain}.ebilling.maxio.com"
            : $"https://{Subdomain}.chargify.com";
    }
}
