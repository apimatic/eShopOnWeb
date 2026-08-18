using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Interfaces.Messaging;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>Body of a resend request. The idempotency key may instead be supplied via the Idempotency-Key header.</summary>
public class ResendNotificationBody
{
    public string? IdempotencyKey { get; set; }
}

/// <summary>Resend result: carries the notificationId the resend produced at the top level.</summary>
public class ResendNotificationResponse
{
    public int NotificationId { get; set; }
    public string? DeliveryStatus { get; set; }

    /// <summary>True when the request was an idempotent replay under an already-seen key (nothing new was sent).</summary>
    public bool WasReplay { get; set; }
}

/// <summary>Carrier for the {notificationId} route parameter.</summary>
public class NotificationIdRequest
{
    public int NotificationId { get; init; }

    public NotificationIdRequest(int notificationId) => NotificationId = notificationId;
}

public class ReconciliationLineDto
{
    public string? ProviderMessageSid { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public string? EShopStatus { get; set; }
    public int? NotificationId { get; set; }
    public int? OrderId { get; set; }
    public DateTimeOffset? ProviderDateSent { get; set; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderCount { get; set; }
    public int EShopCount { get; set; }
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EShopOnlyCount { get; set; }
    public List<ReconciliationLineDto> Lines { get; set; } = new();

    public static ReconciliationResponse Create(ReconciliationReport report) => new()
    {
        From = report.From,
        To = report.To,
        ProviderCount = report.ProviderCount,
        EShopCount = report.EShopCount,
        MatchedCount = report.MatchedCount,
        ProviderOnlyCount = report.ProviderOnlyCount,
        EShopOnlyCount = report.EShopOnlyCount,
        Lines = report.Lines.Select(l => new ReconciliationLineDto
        {
            ProviderMessageSid = l.ProviderMessageSid,
            Source = l.Source.ToString(),
            ProviderStatus = l.ProviderStatus,
            EShopStatus = l.EShopStatus,
            NotificationId = l.NotificationId,
            OrderId = l.OrderId,
            ProviderDateSent = l.ProviderDateSent
        }).ToList()
    };
}
