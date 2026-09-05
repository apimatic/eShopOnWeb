namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Binds to the "Maxio" configuration section. Values must be supplied via user-secrets,
/// environment variables, or another external configuration provider - never hard-coded.
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
