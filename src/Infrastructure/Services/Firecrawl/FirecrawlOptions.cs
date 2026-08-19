namespace Microsoft.eShopWeb.Infrastructure.Services.Firecrawl;

/// <summary>
/// Firecrawl connection settings, bound from the <c>Firecrawl</c> configuration section.
/// The API key must be supplied out of band (environment variable / user-secrets) and never
/// committed to the repository.
/// </summary>
public class FirecrawlOptions
{
    public const string SectionName = "Firecrawl";

    /// <summary>Default Firecrawl API base address (from the OpenAPI spec's server URL).</summary>
    public const string DefaultBaseUrl = "https://api.firecrawl.dev/v2";

    /// <summary>Firecrawl API key, bound from <c>Firecrawl:ApiKey</c> (sourced from FIRECRAWL_API_KEY).</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Optional override for the API base address, bound from <c>Firecrawl:BaseUrl</c>. When set,
    /// it is used verbatim for every Firecrawl call; otherwise <see cref="DefaultBaseUrl"/> is used.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The base address actually used for requests.</summary>
    public string ResolvedBaseUrl => string.IsNullOrWhiteSpace(BaseUrl) ? DefaultBaseUrl : BaseUrl!.Trim();
}
