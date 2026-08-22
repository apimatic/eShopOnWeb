using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse()
    {
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public bool Truncated { get; set; }
    public List<ReconciliationItemDto> Matched { get; set; } = new();
    public List<ReconciliationItemDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationItemDto> LocalOnly { get; set; } = new();
}

public class ReconciliationItemDto
{
    public int? NotificationId { get; set; }
    public string? ProviderSid { get; set; }
    public string? Status { get; set; }
    public string? DateSent { get; set; }
    public string? Kind { get; set; }
}
