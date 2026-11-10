namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Settings of the studio's Mindbody integration, bound from the <c>Mindbody:</c> configuration section.
/// </summary>
public class MindbodySettings
{
    public const string SECTION_NAME = "Mindbody";

    public string? ApiKey { get; set; }
    public string? SiteId { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>Optional override of the Mindbody API's base address. Used verbatim when set.</summary>
    public string? BaseUrl { get; set; }
}
