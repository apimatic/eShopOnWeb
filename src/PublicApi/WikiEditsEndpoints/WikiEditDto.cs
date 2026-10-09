using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.WikiEditsEndpoints;

public class WikiEditDto
{
    public string Wiki { get; set; } = string.Empty;
    public string PageTitle { get; set; } = string.Empty;
    public long RevisionId { get; set; }
    public string? Editor { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public List<RevisionSlotDto> Slots { get; set; } = new();
}
