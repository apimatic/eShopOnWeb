using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderCount { get; set; }
    public int EShopCount { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }
    public List<ReconciliationEntryDto> Entries { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public string? Sid { get; set; }
    /// <summary>Matched, ProviderOnly, or EShopOnly.</summary>
    public string Discrepancy { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public string? EShopStatus { get; set; }
    public int? OrderId { get; set; }
    public string? Kind { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}
