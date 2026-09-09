namespace Microsoft.eShopWeb;

/// <summary>
/// Configuration for the Maxio Advanced Billing integration.
/// Bound from the "Maxio" configuration section; values are supplied via user-secrets
/// or environment variables and are never hard-coded.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>
    /// The site API key used as the Basic-auth username.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The Maxio site subdomain (used to derive the API base address unless <see cref="BaseUrl"/> is set).
    /// </summary>
    public string? Subdomain { get; set; }

    /// <summary>
    /// Handle of the product family that holds the subscription plans.
    /// </summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional verbatim API base-address override. When set, it is used as the API base
    /// address instead of deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional hosting region ("US" or "EU"). Defaults to US. Populated from the
    /// MAXIO_ENVIRONMENT environment variable when present.
    /// </summary>
    public string? Environment { get; set; }
}
