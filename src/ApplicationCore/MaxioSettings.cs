namespace Microsoft.eShopWeb;

/// <summary>
/// Bound from the "Maxio" configuration section. Values are sourced from environment
/// variables / user-secrets at deployment time - never hard-code them.
/// </summary>
public class MaxioSettings
{
    public const string ConfigSectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set, used verbatim as the Maxio API base address instead of
    /// deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
