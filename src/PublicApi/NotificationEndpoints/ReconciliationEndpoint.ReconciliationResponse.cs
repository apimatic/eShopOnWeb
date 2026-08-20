using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationItemDto
{
    public int? NotificationId { get; set; }
    public string? ProviderSid { get; set; }
    public string? Status { get; set; }
    public string? Body { get; set; }
    public string? DateSent { get; set; }
    public int? ErrorCode { get; set; }
    public string? Source { get; set; }
}

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public bool Truncated { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int ApplicationOnlyCount { get; set; }
    public List<ReconciliationItemDto> Matched { get; set; } = new();
    public List<ReconciliationItemDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationItemDto> ApplicationOnly { get; set; } = new();
}
