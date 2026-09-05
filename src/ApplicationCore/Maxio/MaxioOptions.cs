namespace Microsoft.eShopWeb.ApplicationCore.Maxio;

/// <summary>
/// Binds to the "Maxio" configuration section. Values come from user-secrets/environment
/// in every environment - never hard-code a site's ApiKey, Subdomain or ProductFamilyHandle.
/// </summary>
public class MaxioOptions
{
    public const string SectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override for the API base address. When set, it is used verbatim instead of
    /// deriving "https://{Subdomain}.chargify.com" from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
