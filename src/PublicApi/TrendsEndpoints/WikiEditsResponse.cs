using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.TrendsEndpoints;

public class WikiEditsResponse
{
    public int Received { get; set; }
    public List<WikiEditItem> Matches { get; set; } = [];
    public List<WikiEditItem> LatestCommons { get; set; } = [];
    public string StoppedBecause { get; set; } = string.Empty;
}

public class WikiEditItem
{
    public string Wiki { get; set; } = string.Empty;
    public string PageTitle { get; set; } = string.Empty;
    public int RevisionId { get; set; }
    public string Editor { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public List<RevisionSlotInfo> Slots { get; set; } = [];
}

public class RevisionSlotInfo
{
    public string Name { get; set; } = string.Empty;
    public string ContentModel { get; set; } = string.Empty;
    public int SizeBytes { get; set; }
}
