using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Marketplace;

/// <summary>
/// Walmart Marketplace connection settings, bound from the <c>Walmart:</c> configuration section.
/// Values are supplied by the deployment (environment / user-secrets) and never hard-coded.
/// </summary>
public class WalmartSettings
{
    public const string ConfigSection = "Walmart";

    /// <summary>Walmart client id (<c>Walmart:ClientId</c>, from <c>WALMART_CLIENT_ID</c>).</summary>
    [Required(AllowEmptyStrings = false)]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>Walmart client secret (<c>Walmart:ClientSecret</c>, from <c>WALMART_CLIENT_SECRET</c>). Secret — never logged or returned.</summary>
    [Required(AllowEmptyStrings = false)]
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Optional base-URL override (<c>Walmart:BaseUrl</c>), of the form <c>scheme://host[:port]</c>.
    /// When set it replaces the scheme/host/port of every Walmart call (including token acquisition),
    /// keeping the contract's path. Left unset, the integration targets Walmart production.
    /// </summary>
    public string? BaseUrl { get; set; }
}
