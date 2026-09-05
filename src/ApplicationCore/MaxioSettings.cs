namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Binds to the "Maxio" configuration section. Values are supplied via user-secrets/environment
/// variables in every environment this runs in - never hard-code real values here.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set, used verbatim as the Maxio API base address instead of
    /// deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
