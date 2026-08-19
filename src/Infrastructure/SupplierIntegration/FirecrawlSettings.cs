namespace Microsoft.eShopWeb.Infrastructure.SupplierIntegration;

/// <summary>
/// Configuration for the Firecrawl integration, bound from the "Firecrawl" configuration section.
/// The API key must be supplied out-of-band (environment variable / user-secrets) and never
/// committed to the repository.
/// </summary>
public class FirecrawlSettings
{
    public const string ConfigurationSection = "Firecrawl";

    /// <summary>Firecrawl API key. Bound from <c>Firecrawl:ApiKey</c> (sourced from <c>FIRECRAWL_API_KEY</c>).</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Optional override of the Firecrawl API base address (<c>Firecrawl:BaseUrl</c>). When set it is
    /// used verbatim as the base for every Firecrawl call; when empty the public default is used.
    /// </summary>
    public string? BaseUrl { get; set; }
}
