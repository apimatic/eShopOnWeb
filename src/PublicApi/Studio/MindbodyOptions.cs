namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Settings read from the "Mindbody" configuration section.
/// </summary>
public class MindbodyOptions
{
    public const string CONFIG_NAME = "Mindbody";
    public const string DEFAULT_BASE_URL = "https://api.mindbodyonline.com/public/v6";

    public string? ApiKey { get; set; }
    public string? SiteId { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>Optional override; when set it is used verbatim as the API's base address.</summary>
    public string? BaseUrl { get; set; }
}
