namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Settings of the "Mindbody" configuration section. Values come from configuration only
/// (user-secrets / environment); none are hard-coded.
/// </summary>
public class MindbodySettings
{
    public const string CONFIG_NAME = "Mindbody";

    public string? ApiKey { get; set; }
    public string? SiteId { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>Optional override of the API base address; used verbatim when set.</summary>
    public string? BaseUrl { get; set; }
}
