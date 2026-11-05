namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Bound from the "Maxio" configuration section.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional. When set, used verbatim as the Maxio API base address instead of one derived from the subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }
}
