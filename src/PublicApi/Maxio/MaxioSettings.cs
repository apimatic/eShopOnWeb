namespace Microsoft.eShopWeb.PublicApi.Maxio;

/// <summary>
/// Settings bound from the "Maxio" configuration section.
/// Values are supplied via user-secrets or environment variables; never hard-coded.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    public string? ApiKey { get; set; }
    public string? Subdomain { get; set; }
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional override. When set, used verbatim as the API base address
    /// instead of deriving one from the subdomain.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        (!string.IsNullOrWhiteSpace(Subdomain) || !string.IsNullOrWhiteSpace(BaseUrl));
}
