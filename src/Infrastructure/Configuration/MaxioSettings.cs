namespace Microsoft.eShopWeb.Infrastructure.Configuration;

/// <summary>
/// Typed options for the Maxio Advanced Billing integration (mirrors <c>CatalogSettings</c> usage, §2.3).
/// Bound from configuration section "Maxio" - the API key comes from .NET user-secrets, never from
/// source or appsettings.json.
/// </summary>
public class MaxioSettings
{
    /// <summary>Maxio/Chargify API key (Basic auth username; password is the literal "x"). Secret - user-secrets only.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The Maxio site subdomain, used to derive the host when <see cref="BaseUrl"/> is not set.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Maxio data-center region - "US" (default) or "EU". Not the deployment target (§2.3).</summary>
    public string Environment { get; set; } = "US";

    /// <summary>
    /// Optional explicit outbound base URL override (prod / dev tenant / local mock). When set, it wins
    /// verbatim over the <see cref="Subdomain"/>-derived host - the resolution order the client must honor (§2.3).
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Handle of the product family that holds the plans and the metered component (e.g. "eshop-subscribe").</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Handle of the hero/default recurring plan (e.g. "eshop-pro").</summary>
    public string DefaultProductHandle { get; set; } = string.Empty;

    /// <summary>Handle of the alternate recurring plan used for UC3 upgrade/downgrade (e.g. "basic-plan").</summary>
    public string AlternateProductHandle { get; set; } = string.Empty;

    /// <summary>Handle of the metered pay-as-you-go component (e.g. "api-call").</summary>
    public string MeteredComponentHandle { get; set; } = string.Empty;
}
