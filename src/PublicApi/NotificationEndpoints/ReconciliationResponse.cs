using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Entities.Messaging;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ReconciliationResponse : BaseResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public ReconciliationCountsDto Counts { get; set; } = new();
    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> InProviderOnly { get; set; } = new();
    public List<ReconciliationEntryDto> InEShopOnly { get; set; } = new();

    public static ReconciliationResponse FromReport(ReconciliationReport report)
        => new()
        {
            From = report.From,
            To = report.To,
            FromNumber = report.FromNumber,
            Counts = new ReconciliationCountsDto
            {
                Matched = report.Matched.Count,
                InProviderOnly = report.InProviderOnly.Count,
                InEShopOnly = report.InEShopOnly.Count
            },
            Matched = report.Matched.Select(ReconciliationEntryDto.From).ToList(),
            InProviderOnly = report.InProviderOnly.Select(ReconciliationEntryDto.From).ToList(),
            InEShopOnly = report.InEShopOnly.Select(ReconciliationEntryDto.From).ToList()
        };
}

public class ReconciliationCountsDto
{
    public int Matched { get; set; }
    public int InProviderOnly { get; set; }
    public int InEShopOnly { get; set; }
}

public class ReconciliationEntryDto
{
    public int? NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? Status { get; set; }
    public DateTimeOffset? DateSent { get; set; }
    public string Match { get; set; } = string.Empty;

    public static ReconciliationEntryDto From(ReconciliationEntry entry)
        => new()
        {
            NotificationId = entry.NotificationId,
            ProviderMessageSid = entry.ProviderMessageSid,
            Status = entry.Status,
            DateSent = entry.DateSent,
            Match = entry.Match
        };
}
