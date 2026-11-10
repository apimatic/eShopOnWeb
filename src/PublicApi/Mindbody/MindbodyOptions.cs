namespace Microsoft.eShopWeb.PublicApi.Mindbody;

/// <summary>
/// Bound from the "Mindbody:" configuration section.
/// </summary>
public class MindbodyOptions
{
    public const string SECTION = "Mindbody";
    public const string DEFAULT_BASE_URL = "https://api.mindbodyonline.com/public/v6";

    public string ApiKey { get; set; } = string.Empty;
    public string SiteId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>Optional override for the API's base address; used verbatim when set.</summary>
    public string? BaseUrl { get; set; }
}
