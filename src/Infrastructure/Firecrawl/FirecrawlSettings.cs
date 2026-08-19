namespace Microsoft.eShopWeb.Infrastructure.Firecrawl;

/// <summary>
/// Binds the <c>Firecrawl</c> configuration section. The API key is supplied out of band (via
/// environment variable / user-secrets) and must never be written into the repository.
/// </summary>
public class FirecrawlSettings
{
    public const string SectionName = "Firecrawl";

    /// <summary>The Firecrawl API key (Bearer). Sourced from configuration key <c>Firecrawl:ApiKey</c>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Optional override for the Firecrawl API base address. When set, it is used verbatim for every
    /// call instead of the SDK default. Sourced from configuration key <c>Firecrawl:BaseUrl</c>.
    /// </summary>
    public string? BaseUrl { get; set; }
}
