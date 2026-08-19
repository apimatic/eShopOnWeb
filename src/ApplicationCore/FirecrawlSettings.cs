namespace Microsoft.eShopWeb.ApplicationCore;

/// <summary>
/// Firecrawl connection settings, bound from the "Firecrawl" configuration section.
/// The API key must be supplied out-of-band (user-secrets / environment), never committed.
/// </summary>
public class FirecrawlSettings
{
    /// <summary>Firecrawl API key. Bound from configuration key <c>Firecrawl:ApiKey</c>.</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Optional override for the Firecrawl API base address (<c>Firecrawl:BaseUrl</c>). When set it is
    /// used verbatim as the base for every call; when empty the public Firecrawl endpoint is used.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>The public Firecrawl API base address used when no override is configured.</summary>
    public const string DefaultBaseUrl = "https://api.firecrawl.dev";
}
