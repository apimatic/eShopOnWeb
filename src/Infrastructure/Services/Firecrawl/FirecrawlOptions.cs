namespace Microsoft.eShopWeb.Infrastructure.Services.Firecrawl;

/// <summary>
/// Configuration for the Firecrawl API, bound from the <c>Firecrawl</c> configuration section.
/// The API key is supplied out-of-band (environment variable loaded into user-secrets) and is
/// never stored in the repository.
/// </summary>
public class FirecrawlOptions
{
    public const string SectionName = "Firecrawl";

    /// <summary>
    /// Default API base address. Taken from the <c>servers</c> entry of the Firecrawl OpenAPI
    /// specification (<c>https://api.firecrawl.dev/v2</c>). Used only when <see cref="BaseUrl"/>
    /// is not provided.
    /// </summary>
    public const string DefaultBaseUrl = "https://api.firecrawl.dev/v2";

    /// <summary>Firecrawl API key. Bound from <c>Firecrawl:ApiKey</c> (fed by FIRECRAWL_API_KEY).</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Optional override for the API base address, bound from <c>Firecrawl:BaseUrl</c>. When set,
    /// it is used verbatim for every Firecrawl call; otherwise <see cref="DefaultBaseUrl"/> applies.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Maximum time to wait for an extract job to finish before giving up.</summary>
    public int ExtractTimeoutSeconds { get; set; } = 180;

    /// <summary>How often to poll an in-progress extract job.</summary>
    public int ExtractPollIntervalSeconds { get; set; } = 3;

    /// <summary>The base address actually used, honoring the override when present.</summary>
    public string EffectiveBaseUrl =>
        string.IsNullOrWhiteSpace(BaseUrl) ? DefaultBaseUrl : BaseUrl!.Trim();
}
