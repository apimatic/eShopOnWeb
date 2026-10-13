namespace Microsoft.eShopWeb.Infrastructure.Marketplace.Walmart;

/// <summary>
/// Settings for talking to Walmart Marketplace, bound from the <c>Walmart:</c> configuration
/// section. Values are supplied out of band (environment variables loaded into user-secrets, a
/// key vault, etc.) and are never hard-coded in the repository.
/// </summary>
public class WalmartSettings
{
    public const string SectionName = "Walmart";

    /// <summary>Seller application client id (<c>Walmart:ClientId</c>, from <c>WALMART_CLIENT_ID</c>).</summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Seller application client secret (<c>Walmart:ClientSecret</c>, from <c>WALMART_CLIENT_SECRET</c>).
    /// A secret: never logged, never returned by an endpoint, never written to a source file.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Optional base-URL override in the form <c>scheme://host[:port]</c>. When set, it replaces
    /// the scheme, host and port of every Walmart call (including obtaining an access token);
    /// everything after the host — including the base path the contract's server address carries —
    /// is left exactly as the contract gives it. When unset, the contract's production servers are
    /// used.
    /// </summary>
    public string? BaseUrl { get; set; }
}
