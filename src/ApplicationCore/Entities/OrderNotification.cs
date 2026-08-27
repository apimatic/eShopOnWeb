using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities;

public enum NotificationKind
{
    OrderPlaced = 0,
    OrderDispatched = 1,
    DeliveryFollowUp = 2,
    OrderCancelled = 3
}

/// <summary>
/// Record of a single SMS notification sent (or scheduled) for an order,
/// carrying the provider's identifier and latest known delivery outcome.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, string toNumber, string body, NotificationKind kind,
        DateTimeOffset? scheduledFor = null, int? resendOfNotificationId = null, string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        ToNumber = toNumber;
        Body = body;
        Kind = kind;
        ScheduledFor = scheduledFor;
        ResendOfNotificationId = resendOfNotificationId;
        IdempotencyKey = idempotencyKey;
        Status = "pending";
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string ToNumber { get; private set; }
    public string? Body { get; private set; }
    public NotificationKind Kind { get; private set; }

    /// <summary>The provider's own identifier for the message (Twilio Message SID).</summary>
    public string? MessageSid { get; private set; }

    /// <summary>The provider's latest known delivery status (queued, sent, delivered, undelivered, ...).</summary>
    public string Status { get; private set; }
    public int? ErrorCode { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastSyncedAt { get; private set; }
    public bool ContentRedacted { get; private set; }

    /// <summary>Set when this notification was produced by an operator re-send.</summary>
    public int? ResendOfNotificationId { get; private set; }

    /// <summary>Caller-supplied idempotency key for operator re-sends.</summary>
    public string? IdempotencyKey { get; private set; }

    public void MarkSubmitted(string messageSid, string providerStatus)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        MessageSid = messageSid;
        Status = providerStatus;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void MarkSubmissionFailed(string status, int? errorCode)
    {
        Status = status;
        ErrorCode = errorCode;
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void UpdateDeliveryStatus(string status, int? errorCode)
    {
        Status = status;
        if (errorCode.HasValue)
        {
            ErrorCode = errorCode;
        }
        LastSyncedAt = DateTimeOffset.UtcNow;
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }
}
