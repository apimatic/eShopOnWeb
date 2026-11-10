namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>Bound from the "Mindbody:" configuration section.</summary>
public class MindbodySettings
{
    public const string SECTION = "Mindbody";
    public const string DEFAULT_BASE_URL = "https://api.mindbodyonline.com/public/v6";

    public string? ApiKey { get; set; }
    public string? SiteId { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>Optional override; used verbatim as the API base address when set.</summary>
    public string? BaseUrl { get; set; }
}
