using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed configuration for the Maxio Advanced Billing integration (mirrors <c>CatalogSettings</c>).
/// Bound from the "Maxio" configuration section — <see cref="ApiKey"/> comes from user-secrets,
/// never from a checked-in file.
/// </summary>
public class MaxioSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Maxio data-center region ("US" or "EU") — a different axis from <see cref="BaseUrl"/>, the deployment target.</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Optional explicit override for the outbound base URL. When set, it wins over the
    /// <see cref="Subdomain"/>-derived host, so the same build can target production, a
    /// dev/sandbox tenant, or a local mock server purely through configuration.
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

    private bool IsEuRegion => string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Applies this configuration to the SDK's client options, honoring the required precedence:
    /// an explicit <see cref="BaseUrl"/> always wins; only when it is absent is the host derived
    /// from <see cref="Subdomain"/> + region. Never silently falls back to a hardcoded host.
    /// </summary>
    public void ApplyTo(MaxioAdvancedBillingClientOptions options)
    {
        options.Environment = IsEuRegion ? ServerEnvironment.Eu : ServerEnvironment.Us;
        options.BasicAuth = new BasicAuthCredentials
        {
            Username = ApiKey,
            Password = "x"
        };

        if (!string.IsNullOrWhiteSpace(BaseUrl))
        {
            if (IsEuRegion)
            {
                options.Server.Production.Eu.BaseUrl = BaseUrl;
            }
            else
            {
                options.Server.Production.Us.BaseUrl = BaseUrl;
            }
        }
        else
        {
            if (IsEuRegion)
            {
                options.Server.Production.Eu.Site = Subdomain;
            }
            else
            {
                options.Server.Production.Us.Site = Subdomain;
            }
        }
    }
}
