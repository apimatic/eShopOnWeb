using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.eShopWeb.ApplicationCore.Interfaces;

/// <summary>
/// Reads a supplier's product listing page (and any paginated siblings) and extracts the
/// products found on it. Implemented against the Firecrawl OpenAPI contract; callers deal only
/// in the domain shapes below and never see Firecrawl's wire format.
/// </summary>
public interface IFirecrawlProductScraper
{
    /// <summary>
    /// Starts an asynchronous extraction of the products published at <paramref name="listingUrl"/>.
    /// Returns the identifier of the extraction job to poll with <see cref="GetExtractionAsync"/>.
    /// </summary>
    Task<string> StartExtractionAsync(string listingUrl, CancellationToken cancellationToken = default);

    /// <summary>Retrieves the current state and (when finished) the extracted products of a job.</summary>
    Task<ProductExtractionResult> GetExtractionAsync(string jobId, CancellationToken cancellationToken = default);
}

/// <summary>State of a Firecrawl extraction job, as reported by the extract-status contract.</summary>
public enum ExtractionState
{
    Processing,
    Completed,
    Failed,
    Cancelled
}

/// <summary>A product as read from a supplier's listing, before it is matched into the catalog.</summary>
public record ScrapedProduct(
    string? ExternalId,
    string? Name,
    string? Description,
    decimal? Price,
    string? Brand);

/// <summary>Outcome of polling a Firecrawl extraction job.</summary>
public record ProductExtractionResult(
    ExtractionState State,
    IReadOnlyList<ScrapedProduct> Products,
    string? ErrorMessage = null)
{
    public bool IsFinished => State is ExtractionState.Completed or ExtractionState.Failed or ExtractionState.Cancelled;
}
