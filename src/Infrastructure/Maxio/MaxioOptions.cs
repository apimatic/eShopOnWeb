namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration.
/// Bound from the "Maxio" configuration section. Values are supplied via
/// user-secrets or environment variables and must never be hard-coded:
///   Maxio:ApiKey                 - Maxio API key (basic-auth username)
///   Maxio:Subdomain              - Maxio site subdomain
///   Maxio:ProductFamilyHandle    - handle of the product family holding the plans
///   Maxio:BaseUrl                - optional verbatim API base URL override
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;

    public string Subdomain { get; set; } = string.Empty;

    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional. When set, used verbatim as the API base address instead of
    /// deriving one from <see cref="Subdomain"/>. Useful for EU-hosted sites
    /// ({site}.ebilling.maxio.com) or gateways/proxies.
    /// </summary>
    public string? BaseUrl { get; set; }
}