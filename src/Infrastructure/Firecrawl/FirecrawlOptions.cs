namespace Microsoft.eShopWeb.Infrastructure.Firecrawl;

/// <summary>
/// Firecrawl configuration, bound from the <c>Firecrawl</c> configuration section.
/// The API key must be supplied via configuration/secrets (never hard-coded).
/// </summary>
public class FirecrawlOptions
{
    public const string SectionName = "Firecrawl";

    /// <summary>Bearer API key. Bound from <c>Firecrawl:ApiKey</c> (sourced from the FIRECRAWL_API_KEY env var / user-secrets).</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Optional API base-address override. When set (<c>Firecrawl:BaseUrl</c>), it is used verbatim
    /// as the base address for every Firecrawl call; when empty the SDK default is used.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>How often to poll an extract job for completion.</summary>
    public int PollIntervalSeconds { get; set; } = 3;

    /// <summary>Maximum time to wait for an extract job to complete before treating the read as partial.</summary>
    public int ExtractionTimeoutSeconds { get; set; } = 240;
}
