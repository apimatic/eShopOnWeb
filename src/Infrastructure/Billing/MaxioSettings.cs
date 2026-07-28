namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Strongly-typed view of the <c>Maxio:</c> configuration section. Values are never hard-coded —
/// they are supplied by configuration (user-secrets / environment) so the same build runs against
/// a different Maxio site and catalog.
/// </summary>
public class MaxioSettings
{
    public const string SectionName = "Maxio";

    /// <summary>Maxio API key (bound from <c>Maxio:ApiKey</c>). Used as the Basic-auth username.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Site subdomain (bound from <c>Maxio:Subdomain</c>). Derives the API base URL unless overridden.</summary>
    public string? Subdomain { get; set; }

    /// <summary>Product-family handle whose plans are exposed (bound from <c>Maxio:ProductFamilyHandle</c>).</summary>
    public string? ProductFamilyHandle { get; set; }

    /// <summary>
    /// Optional explicit API base-URL override (bound from <c>Maxio:BaseUrl</c>). When set it is used
    /// verbatim instead of deriving the address from <see cref="Subdomain"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Optional default plan (product handle) used when a subscribe request omits one
    /// (bound from <c>Maxio:DefaultPlanHandle</c>).
    /// </summary>
    public string? DefaultPlanHandle { get; set; }

    /// <summary>
    /// Optional Maxio hosting region, <c>US</c> (default) or <c>EU</c> (bound from <c>Maxio:Environment</c>).
    /// </summary>
    public string? Environment { get; set; }
}
