namespace Microsoft.eShopWeb.PublicApi.StudioEndpoints;

/// <summary>
/// Settings read from the "Mindbody:" configuration section.
/// </summary>
public class MindbodyOptions
{
    public const string SECTION_NAME = "Mindbody";

    public string? ApiKey { get; set; }
    public string? SiteId { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>
    /// Optional override of the API's base address; used verbatim when set.
    /// </summary>
    public string? BaseUrl { get; set; }
}
