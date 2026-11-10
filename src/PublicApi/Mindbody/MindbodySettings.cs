namespace Microsoft.eShopWeb.PublicApi.Mindbody;

/// <summary>
/// Bound from the "Mindbody:" configuration section.
/// </summary>
public class MindbodySettings
{
    public const string CONFIG_NAME = "Mindbody";
    public const string DEFAULT_BASE_URL = "https://api.mindbodyonline.com/public/v6";

    public string? ApiKey { get; set; }
    public string? SiteId { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? BaseUrl { get; set; }
}
