namespace Microsoft.eShopWeb;

/// <summary>
/// Binds the "Maxio" configuration section. ApiKey/Subdomain/ProductFamilyHandle come from
/// environment-sourced user-secrets in development; BaseUrl is an optional override that, when
/// set, is used verbatim as the API base address instead of one derived from Subdomain.
/// </summary>
public class MaxioSettings
{
    public const string CONFIG_NAME = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;
    public string? BaseUrl { get; set; }
}
