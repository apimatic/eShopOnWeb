using System;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed configuration for the Maxio Advanced Billing integration (mirror <c>CatalogSettings</c>
/// usage — §2.3). Bound from the "Maxio" configuration section; the API key comes from
/// .NET user-secrets, never from source or <c>appsettings.json</c>.
/// </summary>
public class MaxioSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Maxio data-center region ("US" or "EU") — a separate axis from <see cref="BaseUrl"/>, which is the deployment target (§2.3).</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Optional explicit override for the outbound base URL. When set, it wins verbatim over the
    /// <see cref="Subdomain"/>-derived host — this is the single knob that lets the identical build
    /// target production, a dev/sandbox tenant, or a local mock server (§2.3). Leave empty to derive
    /// the host from <see cref="Subdomain"/> and <see cref="Environment"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ProductFamilyHandle { get; set; } = string.Empty;
    public long ProductFamilyId { get; set; }

    public string DefaultProductHandle { get; set; } = string.Empty;
    public long DefaultProductId { get; set; }

    public string AlternateProductHandle { get; set; } = string.Empty;
    public long AlternateProductId { get; set; }

    public string MeteredComponentHandle { get; set; } = string.Empty;
    public long MeteredComponentId { get; set; }

    public bool IsEuEnvironment => string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Resolution order (§2.3, hard requirement): an explicit <see cref="BaseUrl"/> wins verbatim;
    /// only when it is absent is the host derived from <see cref="Subdomain"/> and <see cref="Environment"/>.
    /// </summary>
    public string ResolveBaseUrl() =>
        string.IsNullOrWhiteSpace(BaseUrl)
            ? (IsEuEnvironment ? $"https://{Subdomain}.ebilling.maxio.com" : $"https://{Subdomain}.chargify.com")
            : BaseUrl;
}
