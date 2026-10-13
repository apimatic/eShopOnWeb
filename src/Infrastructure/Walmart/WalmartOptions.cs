namespace Microsoft.eShopWeb.Infrastructure.Walmart;

/// <summary>
/// Settings for the Walmart Marketplace integration, bound from the <c>Walmart:</c> configuration
/// section. Values are supplied via configuration / user-secrets / environment — never hard-coded.
/// </summary>
public class WalmartOptions
{
    public const string SectionName = "Walmart";

    /// <summary>OAuth client id (from the <c>Walmart:ClientId</c> key / <c>WALMART_CLIENT_ID</c>).</summary>
    public string? ClientId { get; set; }

    /// <summary>OAuth client secret (from <c>Walmart:ClientSecret</c> / <c>WALMART_CLIENT_SECRET</c>). Secret — never logged or returned.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Optional base-URL override, <c>scheme://host[:port]</c>. When set, it replaces the scheme,
    /// host and port of every Walmart call (including the token call); everything after the host —
    /// including the base path the contract carries — is preserved from the contract.
    /// </summary>
    public string? BaseUrl { get; set; }

    public bool HasCredentials => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}
