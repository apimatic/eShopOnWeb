using System;
using System.Collections.Generic;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string SendingNumber { get; set; } = string.Empty;
    public bool Truncated { get; set; }
    public List<ReconciliationItemDto> Matched { get; set; } = new();
    public List<ReconciliationItemDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationItemDto> EshopOnly { get; set; } = new();
}

public class ReconciliationItemDto
{
    public int? NotificationId { get; set; }
    public string? ProviderSid { get; set; }
    public string? Status { get; set; }
    public string? DateSent { get; set; }
}
