namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Configuration for the Firecrawl integration, bound from the <c>Firecrawl:</c> section.
/// The API key is supplied out-of-band (env var / user-secrets) and never stored in the repo.
/// </summary>
public class FirecrawlSettings
{
    public const string SectionName = "Firecrawl";

    /// <summary>Bearer token for the Firecrawl API. Bound from <c>Firecrawl:ApiKey</c>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Optional override for the Firecrawl API base address. When set it is used verbatim for every
    /// call; when empty the client falls back to the base URL declared by the Firecrawl OpenAPI spec.
    /// Bound from <c>Firecrawl:BaseUrl</c>.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>How often to poll an in-flight extraction job for completion.</summary>
    public int PollIntervalSeconds { get; set; } = 3;

    /// <summary>Maximum time to wait for an extraction job to finish before giving up.</summary>
    public int PollTimeoutSeconds { get; set; } = 300;
}
