using System;
using System.Collections.Generic;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS notification sent (or scheduled) for an order, carrying the
/// provider-owned state (message SID and latest known delivery outcome) so a
/// later request can act on it and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Terminal provider statuses; anything else may still change and is worth refreshing.
    private static readonly HashSet<string> TerminalStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "delivered", "undelivered", "failed", "canceled", "read"
    };

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string buyerId, int contactNumberId,
        NotificationType notificationType, string body,
        DateTimeOffset? scheduledFor = null,
        string? idempotencyKey = null,
        int? resendOfNotificationId = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        NotificationType = notificationType;
        Body = body;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        ResendOfNotificationId = resendOfNotificationId;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int ContactNumberId { get; private set; }
    public NotificationType NotificationType { get; private set; }

    /// <summary>Message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    public string? ProviderMessageSid { get; private set; }
    public string? ProviderStatus { get; private set; }
    public int? ProviderErrorCode { get; private set; }

    public bool ContentRedacted { get; private set; }

    /// <summary>Caller-supplied key for idempotent resend requests.</summary>
    public string? IdempotencyKey { get; private set; }

    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset? ScheduledFor { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastStatusRefreshAt { get; private set; }

    public bool IsTerminal =>
        ProviderStatus != null && TerminalStatuses.Contains(ProviderStatus);

    public bool IsScheduled =>
        string.Equals(ProviderStatus, "scheduled", StringComparison.OrdinalIgnoreCase);

    public void MarkAccepted(string providerMessageSid, string providerStatus)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        ProviderStatus = providerStatus;
    }

    public void MarkSendFailed(string? providerStatus, int? errorCode)
    {
        ProviderStatus = providerStatus ?? "failed";
        ProviderErrorCode = errorCode;
    }

    public void UpdateProviderStatus(string? providerStatus, int? errorCode)
    {
        if (string.IsNullOrEmpty(providerStatus))
        {
            return;
        }

        // Never regress a terminal status on stale provider data.
        if (IsTerminal && !TerminalStatuses.Contains(providerStatus))
        {
            return;
        }

        ProviderStatus = providerStatus;
        ProviderErrorCode = errorCode;
        LastStatusRefreshAt = DateTimeOffset.UtcNow;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }
}
