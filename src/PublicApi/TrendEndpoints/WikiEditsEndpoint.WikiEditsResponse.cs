using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.TrendEndpoints;

public class WikiEditsResponse : BaseResponse
{
    public WikiEditsResponse(Guid correlationId) : base(correlationId)
    {
    }

    public WikiEditsResponse()
    {
    }

    /// <summary>
    /// How long the stream was watched for, as requested.
    /// </summary>
    public int Seconds { get; set; }

    /// <summary>
    /// Revision events received in total, from every wiki.
    /// </summary>
    public int Received { get; set; }

    /// <summary>
    /// Received events that could not be read as a page revision (included in <see cref="Received"/>).
    /// </summary>
    public int Unreadable { get; set; }

    /// <summary>
    /// English Wikipedia and Wikimedia Commons edits whose title mentions a catalog brand or type.
    /// </summary>
    public List<WikiEditMatchDto> Matches { get; set; } = new List<WikiEditMatchDto>();

    /// <summary>
    /// Total number of matching edits; more than <see cref="Matches"/> holds when <see cref="MatchesTruncated"/> is true.
    /// </summary>
    public int MatchCount { get; set; }
    public bool MatchesTruncated { get; set; }

    /// <summary>
    /// The last five Wikimedia Commons edits received, oldest first, whatever their title.
    /// </summary>
    public List<WikiEditDto> LatestCommons { get; set; } = new List<WikiEditDto>();

    /// <summary>
    /// "time-limit", "no-data" or "stream-error".
    /// </summary>
    public string StoppedBecause { get; set; } = string.Empty;

    /// <summary>
    /// Why the stream failed; set only when <see cref="StoppedBecause"/> is "stream-error".
    /// </summary>
    public string? Reason { get; set; }
}
