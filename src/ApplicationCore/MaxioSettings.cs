namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Settings for the Maxio Advanced Billing integration, bound from the "Maxio" configuration section.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>The Maxio API key used for HTTP Basic authentication.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>The site subdomain, e.g. "cp-exp-4". Used to derive the API base address.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>The API handle of the product family that contains the subscription plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim instead of
    /// deriving one from the subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }
}
