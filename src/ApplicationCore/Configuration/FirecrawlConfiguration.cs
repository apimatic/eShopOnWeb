namespace Microsoft.eShopWeb.ApplicationCore.Configuration;

/// <summary>
/// Binds the <c>Firecrawl:</c> configuration section. The API key is supplied out-of-band
/// (environment variable / user-secrets) and must never be written into the repository.
/// </summary>
public class FirecrawlConfiguration
{
    public const string CONFIG_NAME = "Firecrawl";

    /// <summary>
    /// Firecrawl API key. Bound from <c>Firecrawl:ApiKey</c> (sourced from the
    /// <c>FIRECRAWL_API_KEY</c> environment variable / user-secrets).
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// Optional override for the Firecrawl API base address. When set, it is used verbatim as
    /// the base address for every Firecrawl call instead of the default public endpoint.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>Default public Firecrawl API base address used when <see cref="BaseUrl"/> is not set.</summary>
    public const string DefaultBaseUrl = "https://api.firecrawl.dev";

    /// <summary>The effective base address: the override when present, otherwise the public default.</summary>
    public string EffectiveBaseUrl =>
        string.IsNullOrWhiteSpace(BaseUrl) ? DefaultBaseUrl : BaseUrl!.Trim();
}
