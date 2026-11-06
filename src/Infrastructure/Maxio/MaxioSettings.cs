namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Bound from the "Maxio" configuration section. No values live in source or committed settings files.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }

    /// <summary>Optional. When set it is used verbatim as the API base address instead of one derived from the subdomain.</summary>
    public string? BaseUrl { get; set; }
}
