namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Settings for connecting to Maxio Advanced Billing. Bound from the "Maxio" configuration
/// section. <see cref="ApiKey"/> must be supplied via user-secrets or environment configuration,
/// never committed to source control.
/// </summary>
public class MaxioOptions
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>
    /// Private API key for the Maxio site, used as the username in HTTP Basic auth.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio site subdomain, e.g. "cp-exp-1". Used to derive the API base address
    /// when <see cref="BaseUrl"/> is not set.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// Handle of the product family that contains the subscribable plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, used verbatim instead of
    /// deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
