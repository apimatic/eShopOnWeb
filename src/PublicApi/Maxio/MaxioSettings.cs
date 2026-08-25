namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings bound from the "Maxio" configuration section. Secret values (ApiKey)
/// are supplied via user-secrets or environment variables, never via appsettings files.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional verbatim API base address override. When set, it is used as-is
    /// instead of deriving the base URL from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Maxio environment/region ("US" or "EU"). Defaults to US.
    /// </summary>
    public string Environment { get; set; } = "US";
}
