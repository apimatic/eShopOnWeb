using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// Record of a single SMS notification sent (or attempted) for an order.
/// Carries the provider's own state (message SID and delivery outcome) so a
/// later request can act on it (cancel, redact, resend) and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public int? ContactNumberId { get; private set; }
    public OrderNotificationType NotificationType { get; private set; }

    /// <summary>Provider's message identifier (Twilio Message SID).</summary>
    public string? MessageSid { get; private set; }

    /// <summary>Message text. Cleared when the content is disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>Provider delivery outcome (queued/sent/delivered/undelivered/scheduled/canceled/...) or a local failure marker.</summary>
    public string Status { get; private set; }

    public string? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public bool ContentRedacted { get; private set; }

    /// <summary>Caller-supplied idempotency key for operator-initiated resends.</summary>
    public string? IdempotencyKey { get; private set; }

    // Terminal provider states; anything else may still change and is worth refreshing.
    public static readonly string[] TerminalStatuses = { "delivered", "undelivered", "failed", "canceled" };

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() {}

    public OrderNotification(int orderId, string buyerId, int? contactNumberId,
        OrderNotificationType notificationType, string? body, DateTimeOffset? scheduledFor = null,
        string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));

        OrderId = orderId;
        BuyerId = buyerId;
        ContactNumberId = contactNumberId;
        NotificationType = notificationType;
        Body = body;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        Status = "pending";
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkAccepted(string messageSid, string status)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        MessageSid = messageSid;
        Status = status;
    }

    public void MarkFailed(string? errorCode, string? errorMessage)
    {
        Status = "failed";
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public void UpdateStatus(string status, string? errorCode, string? errorMessage)
    {
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
    }

    public void MarkContentRedacted()
    {
        Body = null;
        ContentRedacted = true;
    }
}
