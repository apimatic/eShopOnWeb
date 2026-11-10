namespace Microsoft.eShopWeb.PublicApi.Mindbody;

/// <summary>
/// Bound from the "Mindbody:" configuration section. Credentials come from user-secrets / environment, never from source.
/// </summary>
public class MindbodyOptions
{
    public const string SECTION = "Mindbody";
    public const string DEFAULT_BASE_URL = "https://api.mindbodyonline.com/public/v6";

    public string? ApiKey { get; set; }
    public string? SiteId { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>Optional override; when set it is used verbatim as the API base address.</summary>
    public string? BaseUrl { get; set; }
}
