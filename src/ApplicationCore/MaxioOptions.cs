namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Bound from the "Maxio" configuration section. Values must come from configuration/user-secrets;
/// never hard-code a site's credentials or catalog handles here.
/// </summary>
public class MaxioOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set, used verbatim as the Billing API base address instead of
    /// deriving one (https://{Subdomain}.chargify.com) from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string GetBaseUrl() => string.IsNullOrWhiteSpace(BaseUrl)
        ? $"https://{Subdomain}.chargify.com"
        : BaseUrl!;
}
