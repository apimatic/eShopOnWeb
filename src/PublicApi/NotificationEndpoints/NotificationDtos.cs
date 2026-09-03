using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>Body of POST /api/notifications/{notificationId}/resend.</summary>
public class ResendNotificationRequest : BaseRequest
{
    public int NotificationId { get; set; }

    /// <summary>Caller-supplied idempotency key: the same key must not send a second message.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>Response of resend — carries the identifier of the message the resend produced.</summary>
public class ResendNotificationResponse
{
    public int NotificationId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? DeliveryStatus { get; set; }
    /// <summary>True when the key had already been used, so no new message was sent.</summary>
    public bool WasReplay { get; set; }
}

/// <summary>Request for DELETE /api/notifications/{notificationId}/content.</summary>
public class NotificationIdRequest : BaseRequest
{
    public int NotificationId { get; set; }
}

// ---------------------------------------------------------------- reconciliation

public class ReconciliationRequest : BaseRequest
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
}

public class ReconciliationMatchDto
{
    public string ProviderMessageSid { get; set; } = string.Empty;
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string? ProviderStatus { get; set; }
    public string? EshopStatus { get; set; }
}

public class ReconciliationProviderOnlyDto
{
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string? Status { get; set; }
    public DateTimeOffset? DateSent { get; set; }
}

public class ReconciliationEshopOnlyDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string? EshopStatus { get; set; }
}

/// <summary>Response of GET /api/notifications/reconciliation. Destination numbers are deliberately not exposed.</summary>
public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public string SendingNumber { get; set; } = string.Empty;
    public int MatchedCount { get; set; }
    public int ProviderOnlyCount { get; set; }
    public int EshopOnlyCount { get; set; }
    public List<ReconciliationMatchDto> Matched { get; set; } = new();
    public List<ReconciliationProviderOnlyDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationEshopOnlyDto> EshopOnly { get; set; } = new();

    public static ReconciliationResponse FromReport(ReconciliationReport report) => new()
    {
        From = report.From,
        To = report.To,
        SendingNumber = report.SendingNumber,
        MatchedCount = report.Matched.Count,
        ProviderOnlyCount = report.ProviderOnly.Count,
        EshopOnlyCount = report.EshopOnly.Count,
        Matched = report.Matched.Select(m => new ReconciliationMatchDto
        {
            ProviderMessageSid = m.ProviderSid,
            NotificationId = m.NotificationId,
            OrderId = m.OrderId,
            ProviderStatus = m.ProviderStatus,
            EshopStatus = m.EshopStatus
        }).ToList(),
        ProviderOnly = report.ProviderOnly.Select(p => new ReconciliationProviderOnlyDto
        {
            ProviderMessageSid = p.ProviderSid,
            Status = p.Status,
            DateSent = p.DateSent
        }).ToList(),
        EshopOnly = report.EshopOnly.Select(e => new ReconciliationEshopOnlyDto
        {
            NotificationId = e.NotificationId,
            OrderId = e.OrderId,
            ProviderMessageSid = e.ProviderSid,
            EshopStatus = e.EshopStatus
        }).ToList()
    };
}
