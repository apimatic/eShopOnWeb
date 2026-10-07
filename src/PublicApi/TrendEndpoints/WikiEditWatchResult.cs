using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.TrendEndpoints;

/// <summary>
/// What one watch of the Wikimedia revision stream saw, and why it stopped.
/// </summary>
public sealed class WikiEditWatchResult
{
    public int Received { get; init; }

    /// <summary>
    /// Received events that could not be read as a page revision (counted in <see cref="Received"/>).
    /// </summary>
    public int Unreadable { get; init; }
    public IReadOnlyList<WikiEditMatchDto> Matches { get; init; } = new List<WikiEditMatchDto>();

    /// <summary>
    /// How many edits matched in total; larger than <see cref="Matches"/> when the match cap was reached.
    /// </summary>
    public int MatchCount { get; init; }
    public bool MatchesTruncated { get; init; }
    public IReadOnlyList<WikiEditDto> LatestCommons { get; init; } = new List<WikiEditDto>();
    public string StoppedBecause { get; init; } = WikiWatchStopReasons.StreamError;

    /// <summary>
    /// Why the stream failed; set only when <see cref="StoppedBecause"/> is <c>stream-error</c>.
    /// </summary>
    public string? Reason { get; init; }
}
