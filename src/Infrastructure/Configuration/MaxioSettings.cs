using System;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed configuration for the Maxio Advanced Billing integration (mirrors <c>CatalogSettings</c>).
/// Bound from the "Maxio" configuration section — secrets (<see cref="ApiKey"/>) come from .NET
/// user-secrets/environment variables, never from appsettings.json or source (§2.3/§5).
/// </summary>
public class MaxioSettings
{
    /// <summary>The Maxio API key (HTTP Basic auth username; password is the SDK's fixed "x").</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, e.g. "apimatic-hackathon". Used only when <see cref="BaseUrl"/> is empty.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>The Maxio data-center region ("US" or "EU") — a separate axis from the deployment target below.</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Optional explicit override for the outbound base URL. When set, it wins verbatim over the
    /// subdomain-derived host — the single knob that lets the identical build target production, a
    /// dev/sandbox tenant, or a local mock server through configuration alone (§2.3).
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
    /// Resolves the outbound base URL per §2.3: an explicit <see cref="BaseUrl"/> wins verbatim;
    /// otherwise the host is derived from <see cref="Subdomain"/> (+ region <see cref="Environment"/>).
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl!;
        }

        var isEu = string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase);
        var host = isEu ? $"{Subdomain}.ebilling.maxio.com" : $"{Subdomain}.chargify.com";
        return $"https://{host}";
    }
}
