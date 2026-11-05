namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Bound from the "Maxio" configuration section. No value is defaulted here: the same build
/// has to run against any Maxio site and catalog.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    /// <summary>Maxio API key (secret; user-secrets / environment only).</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Maxio site subdomain.</summary>
    public string Subdomain { get; set; } = string.Empty;

    /// <summary>Handle of the product family whose products are offered as plans.</summary>
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>Optional API base address; when set it is used verbatim instead of one derived from the subdomain.</summary>
    public string? BaseUrl { get; set; }
}
