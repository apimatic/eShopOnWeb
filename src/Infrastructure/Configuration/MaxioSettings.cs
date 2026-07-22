using System;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed options for the Maxio Advanced Billing integration (bound from the <c>Maxio</c> config
/// section, mirroring <c>CatalogSettings</c>). Holds the provider shape (subdomain, region, family /
/// product / component handles + ids) and the API key (supplied via user-secrets, never committed).
/// <para>
/// The outbound target server is configuration-driven (§2.3): <see cref="ResolveBaseUrl"/> returns
/// an explicit <see cref="BaseUrl"/> when set (prod / dev / mock), otherwise the host derived from
/// <see cref="Subdomain"/> and the data-center <see cref="Environment"/> (region). The same build can
/// therefore be pointed anywhere without a code change.
/// </para>
/// </summary>
public class MaxioSettings
{
    /// <summary>The Maxio API key. Sensitive — supplied through .NET user-secrets, never committed.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The Maxio site subdomain, e.g. <c>apimatic-hackathon</c>.</summary>
    public string? Subdomain { get; set; }

    /// <summary>The Maxio data-center region (US/EU) — NOT the deployment target (see <see cref="BaseUrl"/>).</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Optional explicit outbound base URL. When set it WINS over the subdomain-derived host, so the
    /// same build can target production, a dev/sandbox tenant, or a local mock server. Leave empty to
    /// derive the host from <see cref="Subdomain"/> (+ <see cref="Environment"/>).
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
    /// Resolves the outbound base URL. Resolution order (§2.3): an explicit <see cref="BaseUrl"/> is
    /// used verbatim; only when it is absent is the host derived from <see cref="Subdomain"/> and the
    /// data-center <see cref="Environment"/> (US → <c>*.chargify.com</c>, EU → <c>*.ebilling.maxio.com</c>).
    /// </summary>
    public string ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            return BaseUrl.Trim();
        }

        if (string.IsNullOrWhiteSpace(Subdomain))
        {
            throw new InvalidOperationException(
                "Maxio configuration is incomplete: set either 'Maxio:BaseUrl' or 'Maxio:Subdomain'.");
        }

        var isEu = string.Equals(Environment?.Trim(), "EU", StringComparison.OrdinalIgnoreCase);
        return isEu
            ? $"https://{Subdomain}.ebilling.maxio.com"
            : $"https://{Subdomain}.chargify.com";
    }
}
