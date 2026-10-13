namespace Microsoft.eShopWeb.Infrastructure.Walmart;

/// <summary>
/// Settings for the Walmart Marketplace integration, bound from the <c>Walmart:</c> configuration
/// section. Values are supplied through configuration (user-secrets / environment) and are never
/// hard-coded. <see cref="ClientSecret"/> is a secret and must never be logged or returned.
/// </summary>
public class WalmartSettings
{
    public const string ConfigSection = "Walmart";

    /// <summary>OAuth client id for the Walmart seller account (from <c>WALMART_CLIENT_ID</c>).</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OAuth client secret for the Walmart seller account (from <c>WALMART_CLIENT_SECRET</c>).</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// Optional override of the form <c>scheme://host[:port]</c>. When set, it replaces the scheme,
    /// host and port of every Walmart call (including obtaining an access token); everything after the
    /// host — including the base path the contract's server address carries — is left untouched. When
    /// empty, the contract's own (production) servers are used.
    /// </summary>
    public string? BaseUrl { get; set; }
}
