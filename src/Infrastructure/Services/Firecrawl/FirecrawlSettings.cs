namespace Microsoft.eShopWeb.Infrastructure.Services.Firecrawl;

/// <summary>
/// Binds the <c>Firecrawl</c> configuration section. The API key value is supplied out-of-band
/// (env var <c>FIRECRAWL_API_KEY</c> loaded into .NET user-secrets as <c>Firecrawl:ApiKey</c>) and
/// is never stored in the repository. <see cref="BaseUrl"/> is an optional override: when set,
/// it is used verbatim as the API base address instead of the SDK default.
/// </summary>
public class FirecrawlSettings
{
    public const string SectionName = "Firecrawl";

    /// <summary>Firecrawl API key (Bearer token). Required for the integration to function.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Optional API base URL override. When null/empty, the SDK default is used.</summary>
    public string? BaseUrl { get; set; }
}
