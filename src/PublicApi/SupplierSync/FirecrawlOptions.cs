namespace Microsoft.eShopWeb.PublicApi.CatalogSync;

/// <summary>
/// Firecrawl connection settings, bound from the <c>Firecrawl</c> configuration section.
/// <see cref="ApiKey"/> comes from the <c>FIRECRAWL_API_KEY</c> environment variable (loaded via
/// user-secrets); <see cref="BaseUrl"/> is an optional override for the API base address.
/// </summary>
public class FirecrawlOptions
{
    public const string SectionName = "Firecrawl";

    /// <summary>The default Firecrawl API base address when <see cref="BaseUrl"/> is not set.</summary>
    public const string DefaultBaseUrl = "https://api.firecrawl.dev";

    public string? ApiKey { get; set; }

    /// <summary>
    /// Optional override. When set, it is used verbatim as the API base address for every
    /// Firecrawl call instead of <see cref="DefaultBaseUrl"/>.
    /// </summary>
    public string? BaseUrl { get; set; }

    public string ResolveBaseUrl() =>
        string.IsNullOrWhiteSpace(BaseUrl) ? DefaultBaseUrl : BaseUrl!.Trim();
}
