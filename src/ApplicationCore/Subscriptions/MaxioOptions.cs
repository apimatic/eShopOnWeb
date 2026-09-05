namespace Microsoft.eShopWeb.ApplicationCore.Subscriptions;

/// <summary>
/// Bound from the "Maxio" configuration section. Values must come from configuration
/// (user secrets in development, environment/secret store in production) - never hard-code
/// a site's credentials or catalog, since the same build targets different Maxio sites.
/// </summary>
public class MaxioOptions
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
