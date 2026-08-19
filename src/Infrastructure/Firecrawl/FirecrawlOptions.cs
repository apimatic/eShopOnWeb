namespace Microsoft.eShopWeb.Infrastructure.Firecrawl;

/// <summary>
/// Firecrawl connection settings, bound from the <c>Firecrawl</c> configuration section.
/// The API key is supplied out-of-band (user-secrets / environment) and never stored in the repo.
/// </summary>
public class FirecrawlOptions
{
    public const string SectionName = "Firecrawl";

    /// <summary>
    /// The default API base address, taken from the Firecrawl OpenAPI spec's declared server. Used
    /// whenever <see cref="BaseUrl"/> is not set.
    /// </summary>
    public const string DefaultBaseUrl = "https://api.firecrawl.dev/v2";

    /// <summary>Bearer token for the Firecrawl API (spec auth scheme: HTTP bearer).</summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Optional override of the API base address. When set it is used verbatim for every call;
    /// when empty the spec's default (<see cref="DefaultBaseUrl"/>) is used.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Per-HTTP-call timeout, in seconds.</summary>
    public int RequestTimeoutSeconds { get; set; } = 100;

    /// <summary>How often to poll an extract job for completion, in seconds.</summary>
    public int PollIntervalSeconds { get; set; } = 2;

    /// <summary>How long to wait overall for an extract job to complete before giving up, in seconds.</summary>
    public int PollTimeoutSeconds { get; set; } = 300;

    /// <summary>The effective base address: the override when present, otherwise the spec default.</summary>
    public string ResolveBaseUrl() =>
        string.IsNullOrWhiteSpace(BaseUrl) ? DefaultBaseUrl : BaseUrl.Trim();
}
