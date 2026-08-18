using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Notifications;

namespace Microsoft.eShopWeb.PublicApi.OrderNotificationEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public ReconciliationResponse(Guid correlationId) : base(correlationId)
    {
    }

    public ReconciliationResponse()
    {
    }

    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }

    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }

    /// <summary>Messages present in both the provider's record and eShop's.</summary>
    public List<ReconciliationEntryDto> Matched { get; set; } = new();

    /// <summary>Messages the provider reports that eShop has no record of.</summary>
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();

    /// <summary>Messages eShop believes it sent that the provider's record for this range does not show.</summary>
    public List<ReconciliationEntryDto> EShopOnly { get; set; } = new();

    public static ReconciliationResponse Create(ReconciliationReport report, Guid correlationId) => new(correlationId)
    {
        From = report.From,
        To = report.To,
        MatchedCount = report.Matched.Count,
        ProviderOnlyCount = report.ProviderOnly.Count,
        EShopOnlyCount = report.EShopOnly.Count,
        Matched = report.Matched.Select(ReconciliationEntryDto.From).ToList(),
        ProviderOnly = report.ProviderOnly.Select(ReconciliationEntryDto.From).ToList(),
        EShopOnly = report.EShopOnly.Select(ReconciliationEntryDto.From).ToList()
    };
}

public class ReconciliationEntryDto
{
    public string? MessageSid { get; set; }
    public string? ProviderStatus { get; set; }
    public string? EShopStatus { get; set; }
    public int? OrderId { get; set; }
    public string? DateSent { get; set; }

    public static ReconciliationEntryDto From(ReconciliationEntry e) => new()
    {
        MessageSid = e.MessageSid,
        ProviderStatus = e.ProviderStatus,
        EShopStatus = e.EShopStatus,
        OrderId = e.OrderId,
        DateSent = e.DateSent
    };
}
