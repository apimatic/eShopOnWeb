using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderAggregate;

/// <summary>
/// Records a single SMS notification attempt for an order: what was sent, to which
/// registered contact number, the provider's message identifier, and the latest known
/// delivery outcome. The provider owns the live state; this record carries enough of it
/// (MessageSid + Status) for later requests to act on and report on the message.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public string RecipientNumber { get; private set; }
    public NotificationType Type { get; private set; }

    /// <summary>Message text. Cleared when the shopper asks for the content to be disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's own identifier for the message (null when the send never reached the provider).</summary>
    public string? MessageSid { get; private set; }

    /// <summary>Latest known provider delivery status (queued, scheduled, sent, delivered, undelivered, failed, canceled).</summary>
    public string Status { get; private set; } = NotificationStatuses.Pending;

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }
    public DateTimeOffset? ScheduledFor { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastUpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public bool ContentRedacted { get; private set; }

    /// <summary>Caller-supplied idempotency key for operator re-sends.</summary>
    public string? IdempotencyKey { get; private set; }
    public int? ResendOfNotificationId { get; private set; }

    public bool IsTerminal =>
        Status is NotificationStatuses.Delivered or NotificationStatuses.Undelivered
            or NotificationStatuses.Failed or NotificationStatuses.Canceled;

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, string recipientNumber,
        NotificationType type, string body, DateTimeOffset? scheduledFor = null,
        string? idempotencyKey = null, int? resendOfNotificationId = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(recipientNumber, nameof(recipientNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        RecipientNumber = recipientNumber;
        Type = type;
        Body = body;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        ResendOfNotificationId = resendOfNotificationId;
    }

    public void RecordAccepted(string messageSid, string providerStatus)
    {
        Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));

        MessageSid = messageSid;
        Status = string.IsNullOrEmpty(providerStatus) ? NotificationStatuses.Queued : providerStatus;
        Touch();
    }

    public void RecordSendFailure(int? providerErrorCode, string? providerErrorMessage)
    {
        Status = NotificationStatuses.Failed;
        ErrorCode = providerErrorCode;
        ErrorMessage = providerErrorMessage;
        Touch();
    }

    public void UpdateProviderStatus(string? providerStatus, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(providerStatus))
        {
            Status = providerStatus;
        }
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        Touch();
    }

    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
        Touch();
    }

    private void Touch() => LastUpdatedAt = DateTimeOffset.UtcNow;
}

public static class NotificationStatuses
{
    public const string Pending = "pending";
    public const string Queued = "queued";
    public const string Scheduled = "scheduled";
    public const string Sent = "sent";
    public const string Delivered = "delivered";
    public const string Undelivered = "undelivered";
    public const string Failed = "failed";
    public const string Canceled = "canceled";
}
