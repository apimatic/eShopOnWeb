using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public bool Truncated { get; set; }
    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationEntryDto> EshopOnly { get; set; } = new();
}

public class ReconciliationEntryDto
{
    public int? NotificationId { get; set; }
    public string? ProviderSid { get; set; }
    public string? ProviderStatus { get; set; }
    public string? EshopStatus { get; set; }
    public int? OrderId { get; set; }
    public string? DateSent { get; set; }
    public string? DateCreated { get; set; }

    public static ReconciliationEntryDto From(ReconciliationEntry entry) => new()
    {
        NotificationId = entry.NotificationId,
        ProviderSid = entry.ProviderSid,
        ProviderStatus = entry.ProviderStatus,
        EshopStatus = entry.EshopStatus,
        OrderId = entry.OrderId,
        DateSent = entry.DateSent,
        DateCreated = entry.DateCreated
    };
}
