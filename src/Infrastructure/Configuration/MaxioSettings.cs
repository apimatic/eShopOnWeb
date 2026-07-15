using System;
using MaxioAdvancedBilling;
using MaxioAdvancedBilling.Core.Authentication.Basic;
using MaxioAdvancedBilling.Servers;

namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed configuration for the Maxio Advanced Billing integration (mirrors <c>CatalogSettings</c>'s
/// Options-binding pattern). Bound from the "Maxio" configuration section — secrets (ApiKey) come from
/// .NET user-secrets, never from source or appsettings.json (plan.md §2.3/§5).
/// </summary>
public class MaxioSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Maxio data-center region (US/EU) — a separate axis from the deployment target controlled by <see cref="BaseUrl"/> (plan.md §2.3).</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Explicit outbound base-URL override. When set, it wins verbatim over <see cref="Subdomain"/> —
    /// this is the hard requirement in plan.md §2.3 that lets the identical build target production,
    /// a dev/sandbox tenant, or a local mock server purely through configuration.
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

    public bool IsEuEnvironment => string.Equals(Environment, "EU", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The single place base-URL/region/auth resolution happens for the SDK client (plan.md §2.2,
    /// §4.3) — resolution order: explicit <see cref="BaseUrl"/> wins verbatim; otherwise the host is
    /// derived from <see cref="Subdomain"/> for the selected <see cref="Environment"/>.
    /// </summary>
    public void Configure(MaxioAdvancedBillingClientOptions options)
    {
        options.BasicAuth = new BasicAuthCredentials
        {
            Username = ApiKey,
            Password = "x"
        };

        if (IsEuEnvironment)
        {
            options.Environment = ServerEnvironment.Eu;
            if (!string.IsNullOrWhiteSpace(BaseUrl))
            {
                options.Server.Production.Eu.BaseUrl = BaseUrl;
            }
            else
            {
                options.Server.Production.Eu.Site = Subdomain;
            }
        }
        else
        {
            options.Environment = ServerEnvironment.Us;
            if (!string.IsNullOrWhiteSpace(BaseUrl))
            {
                options.Server.Production.Us.BaseUrl = BaseUrl;
            }
            else
            {
                options.Server.Production.Us.Site = Subdomain;
            }
        }
    }
}
