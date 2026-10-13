using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.PublicApi.WalmartIntegration;

/// <summary>
/// Settings for the Walmart Marketplace integration, bound from the <c>Walmart:</c> configuration
/// section. Values are supplied by configuration (user-secrets / environment) and are never
/// hard-coded in the repository.
/// </summary>
public class WalmartConfiguration
{
    public const string SectionName = "Walmart";

    /// <summary>Walmart account client id (from <c>Walmart:ClientId</c>).</summary>
    [Required(AllowEmptyStrings = false)]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Walmart account client secret (from <c>Walmart:ClientSecret</c>). Secret — never logged,
    /// never returned by an endpoint, never written into a source file.
    /// </summary>
    [Required(AllowEmptyStrings = false)]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Optional base-URL override in the form <c>scheme://host[:port]</c>. When set, it replaces the
    /// scheme, host and port of every Walmart call (including obtaining an access token); everything
    /// after the host — including the contract's base path — is preserved. Leave empty to use the
    /// SDK's own production base URLs.
    /// </summary>
    public string? BaseUrl { get; set; }
}
