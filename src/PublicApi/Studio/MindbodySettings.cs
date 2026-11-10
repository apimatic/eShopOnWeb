namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Settings of the Mindbody integration, read from the "Mindbody:" configuration section.
/// Credentials are never kept in the repository; use user-secrets or environment variables.
/// </summary>
public class MindbodySettings
{
    public const string SectionName = "Mindbody";

    public string? ApiKey { get; set; }
    public string? SiteId { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>Optional override of the API's base address; used verbatim when set.</summary>
    public string? BaseUrl { get; set; }
}
