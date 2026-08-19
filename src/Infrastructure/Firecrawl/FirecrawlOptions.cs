namespace Microsoft.eShopWeb.Infrastructure.Firecrawl;

/// <summary>
/// Binds the <c>Firecrawl</c> configuration section. The API key is supplied as the
/// <c>Firecrawl:ApiKey</c> value (loaded from the <c>FIRECRAWL_API_KEY</c> environment
/// variable into user-secrets — never committed to the repository). <see cref="BaseUrl"/>
/// is an optional override; when set it is used verbatim as the Firecrawl API base address.
/// </summary>
public class FirecrawlOptions
{
    public const string SectionName = "Firecrawl";

    /// <summary>Firecrawl API key (bearer credential). Bound from <c>Firecrawl:ApiKey</c>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Optional Firecrawl API base URL override (<c>Firecrawl:BaseUrl</c>). When present it is
    /// used verbatim instead of the SDK default.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>How often to poll the extract job for completion.</summary>
    public int PollIntervalSeconds { get; set; } = 2;

    /// <summary>How long to wait for an extract job to complete before giving up.</summary>
    public int PollTimeoutSeconds { get; set; } = 180;
}
