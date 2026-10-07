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

    /// <summary>The watch window that was requested, in seconds.</summary>
    public int Seconds { get; set; }

    /// <summary>Revision events received from every wiki (including any that could not be parsed).</summary>
    public int Received { get; set; }

    /// <summary>
    /// Events that arrived but could not be read — malformed JSON, or an English Wikipedia / Commons revision that
    /// does not fit the revision schema; counted in <see cref="Received"/>.
    /// </summary>
    public int Unparsed { get; set; }

    /// <summary>English Wikipedia / Wikimedia Commons edits whose title mentions a catalog brand or type.</summary>
    public List<WikiEditDto> Matches { get; set; } = new();

    /// <summary>All matching edits seen, which can exceed the number listed in <see cref="Matches"/>.</summary>
    public int TotalMatches { get; set; }

    /// <summary>True when <see cref="Matches"/> stopped at the configured cap.</summary>
    public bool MatchesTruncated { get; set; }

    /// <summary>The last Wikimedia Commons edits received, newest first (at most 5).</summary>
    public List<WikiEditDto> LatestCommons { get; set; } = new();

    /// <summary><c>time-limit</c>, <c>no-data</c> or <c>stream-error</c>.</summary>
    public string StoppedBecause { get; set; } = string.Empty;

    /// <summary>Why the watch stopped early; null for <c>time-limit</c>.</summary>
    public string? Reason { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public long ElapsedMilliseconds { get; set; }
}

public class WikiEditDto
{
    public string Wiki { get; set; } = string.Empty;
    public string PageTitle { get; set; } = string.Empty;
    public long RevisionId { get; set; }
    public string? Editor { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public List<WikiSlotDto> Slots { get; set; } = new();
    public List<string> MatchedTerms { get; set; } = new();
}

public class WikiSlotDto
{
    public string Name { get; set; } = string.Empty;
    public string? ContentModel { get; set; }
    public long? SizeBytes { get; set; }
}
