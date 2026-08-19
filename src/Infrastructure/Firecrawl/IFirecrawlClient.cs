using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.Infrastructure.Firecrawl;

/// <summary>
/// Thin client over the Firecrawl crawl endpoints (<c>POST /crawl</c>, <c>GET /crawl/{id}</c>),
/// built directly against firecrawl-spec/openapi.json. Encapsulates the async crawl protocol:
/// start a crawl, poll it to a terminal state, and page through the full result set.
/// </summary>
internal interface IFirecrawlClient
{
    /// <summary>
    /// Starts a crawl and waits for it to reach a terminal state (<c>completed</c>/<c>failed</c>),
    /// returning the aggregated result across every result page.
    /// </summary>
    Task<FirecrawlCrawlResult> CrawlAsync(CrawlRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Aggregated terminal result of a crawl.</summary>
internal sealed class FirecrawlCrawlResult
{
    /// <summary>Terminal crawl status reported by Firecrawl: <c>completed</c> or <c>failed</c>.</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>Total number of pages Firecrawl attempted to crawl.</summary>
    public int Total { get; init; }

    /// <summary>Number of pages Firecrawl crawled successfully.</summary>
    public int Completed { get; init; }

    /// <summary>Every crawled page's data, accumulated across all result pages.</summary>
    public IReadOnlyList<CrawlDataItem> Data { get; init; } = new List<CrawlDataItem>();
}
