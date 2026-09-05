namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Binds to the "Maxio" configuration section. Values are supplied via user-secrets/environment,
/// never hard-coded, so the same build can target a different Maxio site and catalog.
/// </summary>
public class MaxioOptions
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
