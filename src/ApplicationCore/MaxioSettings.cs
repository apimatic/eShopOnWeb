namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Settings for talking to Maxio Advanced Billing. Bound from the "Maxio" configuration
/// section. Values must come from configuration/user-secrets/environment - never hard-code
/// a real ApiKey, Subdomain, ProductFamilyHandle or BaseUrl, since the same build has to run
/// against different Maxio sites and catalogs.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>
    /// The Billing API key for the site, used as the HTTP Basic Auth username (password is "x").
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The Maxio/Chargify site subdomain, e.g. "your-site" for https://your-site.chargify.com.
    /// </summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>
    /// The handle of the product family that contains the subscribable plans.
    /// </summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional explicit API base address. When set, used verbatim instead of deriving one
    /// from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
