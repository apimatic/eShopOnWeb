using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS message that eShop sent (or scheduled) for an order, together with enough of the
/// state the provider owns — the provider message identifier and the current delivery outcome —
/// that a later request can act on it (resend, cancel, redact) and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string ownerId, string toNumber, NotificationType type, string body)
    {
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        OwnerId = ownerId;
        ToNumber = toNumber;
        Type = type;
        Body = body;
        Status = NotificationStatus.SendError; // until a provider result is recorded
        CreatedDate = DateTimeOffset.UtcNow;
    }

    /// <summary>The order this message is about.</summary>
    public int OrderId { get; private set; }

    /// <summary>Identity of the shopper the message is for (the order's buyer).</summary>
    public string OwnerId { get; private set; }

    /// <summary>Destination number in E.164. Persisted, but never written to logs.</summary>
    public string ToNumber { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>Message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    public bool ContentRedacted { get; private set; }

    /// <summary>The provider's identifier for this message (Twilio message SID). Null if the provider was never reached.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Current, normalized delivery outcome. See <see cref="NotificationStatus"/>.</summary>
    public string Status { get; private set; }

    public int? ErrorCode { get; private set; }
    public string? ErrorMessage { get; private set; }

    /// <summary>True for a message queued with the provider to be sent later (the delivery follow-up).</summary>
    public bool IsScheduled { get; private set; }

    /// <summary>When a scheduled message is due to be sent.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>When the provider reports the message was actually sent.</summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>Caller-supplied idempotency key for the resend that produced this notification, if any.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>Id of the original notification this one is a resend of, if any.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    /// <summary>Records the outcome of an immediate send attempt that reached the provider.</summary>
    public void RecordSendResult(string providerMessageSid, string status, int? errorCode, string? errorMessage, DateTimeOffset? sentAt)
    {
        ProviderMessageSid = providerMessageSid;
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        SentAt = sentAt;
        IsScheduled = false;
    }

    /// <summary>Records that a message was accepted by the provider for later delivery.</summary>
    public void RecordScheduled(string providerMessageSid, string status, DateTimeOffset scheduledSendAt)
    {
        ProviderMessageSid = providerMessageSid;
        Status = status;
        IsScheduled = true;
        ScheduledSendAt = scheduledSendAt;
    }

    /// <summary>Records that the provider could not be reached / refused the request outright.</summary>
    public void RecordSendError(string? errorMessage, int? errorCode = null)
    {
        Status = NotificationStatus.SendError;
        ErrorMessage = errorMessage;
        ErrorCode = errorCode;
    }

    /// <summary>Refreshes the delivery outcome from the provider's current view of the message.</summary>
    public void UpdateDeliveryState(string status, int? errorCode, string? errorMessage, DateTimeOffset? sentAt)
    {
        Status = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        if (sentAt.HasValue)
        {
            SentAt = sentAt;
        }
        if (!string.Equals(status, NotificationStatus.Scheduled, StringComparison.OrdinalIgnoreCase))
        {
            IsScheduled = false;
        }
    }

    /// <summary>Marks a scheduled message as cancelled before it went out.</summary>
    public void MarkCancelled()
    {
        Status = NotificationStatus.Canceled;
        IsScheduled = false;
    }

    /// <summary>Disposes of the message text locally. The provider redaction is performed by the caller.</summary>
    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    /// <summary>Marks this notification as a resend of another, carrying the caller's idempotency key.</summary>
    public void MarkAsResendOf(int originalNotificationId, string idempotencyKey)
    {
        ResendOfNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>True when this message did not reach the shopper and an operator may want to resend it.</summary>
    public bool IsUndelivered =>
        Status is NotificationStatus.Undelivered
                or NotificationStatus.Failed
                or NotificationStatus.SendError;
}
