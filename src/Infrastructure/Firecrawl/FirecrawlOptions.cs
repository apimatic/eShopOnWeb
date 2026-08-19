using System;

namespace Microsoft.eShopWeb.Infrastructure.Firecrawl;

/// <summary>
/// Bound from the <c>Firecrawl</c> configuration section. <see cref="ApiKey"/> comes from the
/// <c>FIRECRAWL_API_KEY</c> environment variable (via user-secrets/config) and is never stored in the
/// repository. <see cref="BaseUrl"/> is an optional override used verbatim as the API base address.
/// </summary>
public class FirecrawlOptions
{
    public const string SectionName = "Firecrawl";

    public string? ApiKey { get; set; }

    public string? BaseUrl { get; set; }

    /// <summary>Overall budget for a single listing extract job (start + poll to completion).</summary>
    public TimeSpan ExtractTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long to wait between extract-status polls.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);
}
