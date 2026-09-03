using System;
using System.Collections.Generic;
using Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.PublicApi.NotificationEndpoints;

/// <summary>
/// The public view of one notification. Carries its own <c>notificationId</c> (what the operator endpoints
/// act on) and the state the provider owns (its message id and current delivery status). The recipient
/// number is deliberately not exposed.
/// </summary>
public class NotificationDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public string? ProviderStatus { get; set; }
    public int? ProviderErrorCode { get; set; }
    public string? ProviderErrorMessage { get; set; }
    public bool ContentRedacted { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public static NotificationDto From(OrderNotification n) => new()
    {
        NotificationId = n.Id,
        OrderId = n.OrderId,
        Kind = n.Kind.ToString(),
        State = n.State.ToString(),
        ProviderMessageSid = n.ProviderMessageSid,
        ProviderStatus = n.ProviderStatus,
        ProviderErrorCode = n.ProviderErrorCode,
        ProviderErrorMessage = n.ProviderErrorMessage,
        ContentRedacted = n.ContentRedacted,
        ScheduledFor = n.ScheduledFor,
        CreatedAt = n.CreatedAt
    };
}

public class ResendNotificationRequest
{
    /// <summary>Caller-supplied idempotency key: a repeat under the same key does not send again.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

public class ResendNotificationResponse
{
    /// <summary>The identifier of the message the resend produced (existing one on an idempotent replay).</summary>
    public int NotificationId { get; set; }
    public string State { get; set; } = string.Empty;
    public string? ProviderMessageSid { get; set; }
    public string? ProviderStatus { get; set; }
}

public class DisposeContentResponse
{
    public int NotificationId { get; set; }
    public bool ContentRedacted { get; set; }
}

public class ReconciliationResponse
{
    public DateTimeOffset From { get; set; }
    public DateTimeOffset To { get; set; }
    public int ProviderMessageCount { get; set; }
    public int EShopMessageCount { get; set; }
    public List<ReconciliationMatchDto> Matched { get; set; } = new();
    public List<ReconciliationProviderOnlyDto> ProviderOnly { get; set; } = new();
    public List<ReconciliationEShopOnlyDto> EShopOnly { get; set; } = new();

    public static ReconciliationResponse FromReport(ReconciliationReport report)
    {
        var response = new ReconciliationResponse
        {
            From = report.From,
            To = report.To,
            ProviderMessageCount = report.ProviderMessageCount,
            EShopMessageCount = report.EShopMessageCount
        };
        foreach (var m in report.Matched)
        {
            response.Matched.Add(new ReconciliationMatchDto
            {
                NotificationId = m.NotificationId,
                OrderId = m.OrderId,
                ProviderMessageSid = m.Sid,
                ProviderStatus = m.ProviderStatus,
                EShopProviderStatus = m.EShopProviderStatus
            });
        }
        foreach (var p in report.ProviderOnly)
        {
            response.ProviderOnly.Add(new ReconciliationProviderOnlyDto
            {
                ProviderMessageSid = p.Sid,
                ProviderStatus = p.Status,
                DateSent = p.DateSent
            });
        }
        foreach (var n in report.EShopOnly)
        {
            response.EShopOnly.Add(new ReconciliationEShopOnlyDto
            {
                NotificationId = n.Id,
                OrderId = n.OrderId,
                ProviderMessageSid = n.ProviderMessageSid,
                ProviderStatus = n.ProviderStatus
            });
        }
        return response;
    }
}

public class ReconciliationMatchDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public string? EShopProviderStatus { get; set; }
}

public class ReconciliationProviderOnlyDto
{
    public string ProviderMessageSid { get; set; } = string.Empty;
    public string? ProviderStatus { get; set; }
    public string? DateSent { get; set; }
}

public class ReconciliationEShopOnlyDto
{
    public int NotificationId { get; set; }
    public int OrderId { get; set; }
    public string? ProviderMessageSid { get; set; }
    public string? ProviderStatus { get; set; }
}
