namespace Microsoft.eShopWeb.Infrastructure.Walmart;

/// <summary>
/// Strongly-typed binding of the <c>Walmart:</c> configuration section. Values are supplied via
/// configuration (user-secrets / environment) and are never hard-coded in the repository.
/// </summary>
public class WalmartSettings
{
    public const string SectionName = "Walmart";

    /// <summary>OAuth client id (from <c>Walmart:ClientId</c>).</summary>
    public string? ClientId { get; set; }

    /// <summary>OAuth client secret (from <c>Walmart:ClientSecret</c>). Treated as a secret: never logged or returned.</summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Optional base-URL override in the form <c>scheme://host[:port]</c> (from
    /// <c>Walmart:BaseUrl</c>). When set, it replaces the scheme/host/port of every Walmart call;
    /// the base path each contract carries after the host is preserved.
    /// </summary>
    public string? BaseUrl { get; set; }
}
