namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Settings read from the <c>Mindbody:</c> configuration section. No values are hard-coded; the sandbox
/// credentials come from .NET user-secrets (or any other configuration provider).
/// </summary>
public class MindbodySettings
{
    public const string CONFIG_NAME = "Mindbody";

    public string? ApiKey { get; set; }
    public string? SiteId { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }

    /// <summary>Optional override of the API's base address; used verbatim when set.</summary>
    public string? BaseUrl { get; set; }
}
