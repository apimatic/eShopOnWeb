using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

public class ResendNotificationResponse
{
    /// <summary>Identifier of the message the resend produced (top-level).</summary>
    public int NotificationId { get; set; }

    /// <summary>The notification this was a resend of.</summary>
    public int? ResendOfNotificationId { get; set; }

    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }

    /// <summary>True when a prior request under the same idempotency key was replayed (no new send).</summary>
    public bool Replayed { get; set; }
}

public class RedactContentResponse
{
    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
}

public class ReconciliationEntryDto
{
    public string Sid { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
    public int? NotificationId { get; set; }
    public string? NotificationType { get; set; }
    public int? OrderId { get; set; }
    public string? EShopStatus { get; set; }

    public static ReconciliationEntryDto From(ReconciliationEntry e) => new()
    {
        Sid = e.Sid,
        ProviderStatus = e.ProviderStatus,
        ProviderDateSent = e.ProviderDateSent,
        NotificationId = e.NotificationId,
        NotificationType = e.NotificationType?.ToString(),
        OrderId = e.OrderId,
        EShopStatus = e.EShopStatus?.ToString()
    };
}

/// <summary>
/// A reconciliation over a date range: the provider's own record of messages from the configured
/// sending number, lined up against what eShop believes it sent. Messages the provider knows about
/// and eShop does not appear in <see cref="ProviderOnly"/>; the reverse in <see cref="EShopOnly"/>.
/// </summary>
public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string FromNumber { get; set; } = string.Empty;
    public int ProviderCount { get; set; }
    public int EShopCount { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }
    public List<ReconciliationEntryDto> Matched { get; set; } = new();
    public List<ReconciliationEntryDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationEntryDto> EShopOnly { get; set; } = new();

    public static ReconciliationResponse FromReport(ReconciliationReport report) => new()
    {
        From = report.From,
        To = report.To,
        FromNumber = report.FromNumber,
        ProviderCount = report.ProviderCount,
        EShopCount = report.EShopCount,
        MatchedCount = report.Matched.Count,
        ProviderOnlyCount = report.ProviderOnly.Count,
        EShopOnlyCount = report.EShopOnly.Count,
        Matched = report.Matched.Select(ReconciliationEntryDto.From).ToList(),
        ProviderOnly = report.ProviderOnly.Select(ReconciliationEntryDto.From).ToList(),
        EShopOnly = report.EShopOnly.Select(ReconciliationEntryDto.From).ToList()
    };
}
