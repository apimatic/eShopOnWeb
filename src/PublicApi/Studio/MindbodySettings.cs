namespace Microsoft.eShopWeb.PublicApi.Studio;

/// <summary>
/// Bound from the "Mindbody:" configuration section. No value is hard-coded; the sandbox credentials
/// come from .NET user-secrets (or environment variables such as Mindbody__ApiKey).
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
