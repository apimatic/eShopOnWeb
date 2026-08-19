namespace Microsoft.eShopWeb.Infrastructure.Services.Firecrawl;

/// <summary>
/// Configuration for the Firecrawl integration, bound from the "Firecrawl" configuration section.
/// The API key value is never stored in the repository; it is supplied at runtime via user-secrets
/// or the FIRECRAWL_API_KEY environment variable.
/// </summary>
public class FirecrawlOptions
{
    public const string ConfigSection = "Firecrawl";

    /// <summary>The Firecrawl API key (e.g. "fc-..."). Required for the integration to function.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Optional override for the Firecrawl API base address. When set it is used verbatim; when
    /// empty the public Firecrawl endpoint is used.
    /// </summary>
    public string? BaseUrl { get; set; }
}
