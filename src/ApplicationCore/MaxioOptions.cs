namespace Microsoft.eShopWeb;

/// <summary>
/// Bound from the "Maxio" configuration section. Values must come from configuration
/// (user-secrets/environment variables) - never hard-code them, since the same build has to
/// run against different Maxio sites and catalogs.
/// </summary>
public class MaxioOptions
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio Advanced Billing API key, used as the Basic Auth username.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain, used to derive the default API base address.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family that contains the subscribable plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim instead of
    /// deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
