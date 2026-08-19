using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces.Firecrawl;

/// <summary>
/// Thin client over the Firecrawl v2 API, built to the OpenAPI contract in <c>firecrawl-spec/</c>.
/// Only the endpoints needed to read a supplier's product listing are exposed.
/// </summary>
public interface IFirecrawlClient
{
    /// <summary>
    /// Starts an asynchronous structured-extraction job (<c>POST /extract</c>). Returns the job id
    /// used to poll for completion.
    /// </summary>
    Task<FirecrawlExtractJob> StartExtractAsync(FirecrawlExtractRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the status and (once complete) the data of an extract job (<c>GET /extract/{id}</c>).
    /// </summary>
    Task<FirecrawlExtractResult> GetExtractStatusAsync(string jobId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Raised when Firecrawl returns an error response or an otherwise unusable payload.
/// </summary>
public sealed class FirecrawlException : System.Exception
{
    public FirecrawlException(string message) : base(message) { }
    public FirecrawlException(string message, System.Exception innerException) : base(message, innerException) { }
}
