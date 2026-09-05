namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Bound from the "Maxio" configuration section. <see cref="ApiKey"/> must come from user-secrets /
/// environment, never a checked-in appsettings value.
/// </summary>
public class MaxioOptions
{
    public const string ConfigSectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override: when set, used verbatim as the API base address instead of deriving one
    /// from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
