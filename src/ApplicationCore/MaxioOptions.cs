namespace Microsoft.eShopWeb;

/// <summary>
/// Settings bound from the <c>Maxio</c> configuration section.
/// Values are supplied via environment variables / user-secrets — never hard-coded.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
}
