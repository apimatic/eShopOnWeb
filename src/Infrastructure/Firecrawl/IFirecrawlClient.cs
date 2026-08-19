using System.Threading;
using System.Threading.Tasks;
using Microsoft.eShopWeb.Infrastructure.Firecrawl.Models;

namespace Microsoft.eShopWeb.Infrastructure.Firecrawl;

/// <summary>
/// A thin, hand-written client for the Firecrawl API, built directly against the OpenAPI spec in
/// <c>firecrawl-spec/</c>. Only the endpoints this integration uses are exposed.
/// </summary>
public interface IFirecrawlClient
{
    /// <summary>
    /// Starts an asynchronous structured-extract job (spec: <c>POST /extract</c>). Returns the job
    /// id to poll for completion.
    /// </summary>
    Task<FirecrawlExtractResponse> StartExtractAsync(FirecrawlExtractRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status and, once complete, the data of an extract job (spec: <c>GET /extract/{id}</c>).
    /// </summary>
    Task<FirecrawlExtractStatusResponse> GetExtractStatusAsync(string jobId, CancellationToken cancellationToken = default);
}
