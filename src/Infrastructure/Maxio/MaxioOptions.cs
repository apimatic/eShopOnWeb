namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Bound from the "Maxio" configuration section. Values must come from configuration/user-secrets/
/// environment variables (never hard-coded) so the same build can target a different Maxio site.
/// </summary>
public class MaxioOptions
{
    public const string ConfigSectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set, used verbatim as the API base address instead of deriving one
    /// from <see cref="Subdomain"/> (e.g. to target an EU-hosted site).
    /// </summary>
    public string? BaseUrl { get; set; }
}
