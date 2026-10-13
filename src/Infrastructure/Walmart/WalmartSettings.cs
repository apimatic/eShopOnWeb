using System.ComponentModel.DataAnnotations;

namespace Microsoft.eShopWeb.Infrastructure.Walmart;

/// <summary>
/// Bound from the <c>Walmart:</c> configuration section. Values come from configuration
/// (user-secrets / environment / secret store) — never hard-coded. The secret is never logged or
/// returned by an endpoint.
/// </summary>
public class WalmartSettings
{
    public const string ConfigSection = "Walmart";

    /// <summary>OAuth2 client id (from the <c>Walmart:ClientId</c> key / WALMART_CLIENT_ID).</summary>
    [Required(AllowEmptyStrings = false)]
    public string? ClientId { get; set; }

    /// <summary>OAuth2 client secret (from the <c>Walmart:ClientSecret</c> key / WALMART_CLIENT_SECRET).</summary>
    [Required(AllowEmptyStrings = false)]
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Optional base-URL override in the form <c>scheme://host[:port]</c>. When set, it replaces the
    /// scheme, host and port of every Walmart call (including token acquisition); the path the
    /// contract carries is kept. Null/empty ⇒ the contract's own hosts are used.
    /// </summary>
    public string? BaseUrl { get; set; }
}
