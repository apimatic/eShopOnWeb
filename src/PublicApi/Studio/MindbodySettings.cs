namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Settings read from the "Mindbody:" configuration section. No value is hard-coded; the sandbox
/// credentials are supplied through user-secrets / environment configuration.
/// </summary>
public class MindbodySettings
{
    public const string SECTION = "Mindbody";

    public string? ApiKey { get; set; }
    public string? SiteId { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>Optional override of the API's base address, used verbatim when set.</summary>
    public string? BaseUrl { get; set; }
}
