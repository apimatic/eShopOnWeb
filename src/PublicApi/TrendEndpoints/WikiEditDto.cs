using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.TrendEndpoints;

/// <summary>
/// One page revision seen on the Wikimedia live stream.
/// </summary>
public class WikiEditDto
{
    /// <summary>
    /// The wiki's domain, e.g. "en.wikipedia.org" or "commons.wikimedia.org".
    /// </summary>
    public string Wiki { get; set; } = string.Empty;
    public string PageTitle { get; set; } = string.Empty;
    public long RevisionId { get; set; }
    public string? Editor { get; set; }
    public DateTimeOffset Timestamp { get; set; }

    /// <summary>
    /// Every content slot the revision carries, "main" first.
    /// </summary>
    public List<WikiEditSlotDto> Slots { get; set; } = new List<WikiEditSlotDto>();
}
