namespace Microsoft.eShopWeb.Infrastructure.Firecrawl;

/// <summary>
/// Binds the <c>Firecrawl:</c> configuration section. The API key is supplied out of band
/// (environment variable / user-secrets) and must never be committed to the repository.
/// </summary>
public class FirecrawlOptions
{
    public const string SectionName = "Firecrawl";

    /// <summary>
    /// The default Firecrawl API base address (the single <c>servers</c> entry declared in the
    /// Firecrawl OpenAPI specification). Used when <see cref="BaseUrl"/> is not set.
    /// </summary>
    public const string DefaultBaseUrl = "https://api.firecrawl.dev/v2";

    /// <summary>Firecrawl API key, bound from <c>Firecrawl:ApiKey</c> (env var <c>FIRECRAWL_API_KEY</c>).</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Optional override for the Firecrawl API base address (<c>Firecrawl:BaseUrl</c>). When set,
    /// it is used verbatim as the base address for every Firecrawl call; when unset, the default
    /// declared in the spec (<see cref="DefaultBaseUrl"/>) is used.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Maximum time to wait for a crawl to reach a terminal state before giving up.</summary>
    public int CrawlTimeoutSeconds { get; set; } = 180;

    /// <summary>Delay between crawl status polls.</summary>
    public int PollIntervalSeconds { get; set; } = 3;

    /// <summary>Maximum number of listing pages to crawl.</summary>
    public int MaxPages { get; set; } = 200;

    public string ResolvedBaseUrl =>
        string.IsNullOrWhiteSpace(BaseUrl) ? DefaultBaseUrl : BaseUrl.Trim();
}
