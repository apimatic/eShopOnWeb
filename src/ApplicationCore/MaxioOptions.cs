namespace Microsoft.eShopWeb;

/// <summary>
/// Binds to the "Maxio" configuration section. Values are supplied via user-secrets/environment
/// configuration - never hard-code them, since the same build must run against a different Maxio
/// site and catalog.
/// </summary>
public class MaxioOptions
{
    public const string CONFIG_NAME = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set, used verbatim as the API base address instead of deriving one
    /// from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
