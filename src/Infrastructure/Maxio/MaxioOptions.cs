namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Binds to the "Maxio" configuration section. Values come from user-secrets/environment,
/// never from source-controlled appsettings.
/// </summary>
public class MaxioOptions
{
    public const string ConfigSectionName = "Maxio";

    public string ApiKey { get; set; } = string.Empty;
    public string Subdomain { get; set; } = string.Empty;
    public string ProductFamilyHandle { get; set; } = string.Empty;

    /// <summary>
    /// Optional override of the API base address. When set, used verbatim instead of
    /// deriving "https://{Subdomain}.chargify.com" from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
