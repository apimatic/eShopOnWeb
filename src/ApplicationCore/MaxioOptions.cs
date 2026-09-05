namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Binds to the "Maxio" configuration section. Values come from user-secrets/environment
/// in every environment; none are checked into source control.
/// </summary>
public class MaxioOptions
{
    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override. When set, used verbatim as the API base address instead of
    /// deriving one from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
