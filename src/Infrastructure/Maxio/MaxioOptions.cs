namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration
/// section. Secret values never live in this repository — they arrive via user-secrets or the
/// environment.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// The Maxio API key — sent as the Basic auth username.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The Maxio site subdomain. Required unless <see cref="BaseUrl"/> overrides the whole address.
    /// </summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// The handle of the product family holding the subscription plans this application offers.
    /// </summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// "US" or "EU". Defaults to US when unset.
    /// </summary>
    public string? Environment { get; set; }

    /// <summary>
    /// Optional verbatim override of the API base address (e.g. a sandbox host or proxy).
    /// When set, it is used instead of the address derived from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}