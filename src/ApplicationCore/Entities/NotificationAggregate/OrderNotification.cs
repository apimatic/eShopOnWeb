using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single text message about an order. It carries enough of the state the provider owns — the
/// provider's message identifier and the current delivery outcome — that a later request can act on it
/// (resend, cancel, dispose content) and report on it, not only the request that first sent it.
/// The recipient number and body are PII and must never be written to logs.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }

    /// <summary>Identity of the shopper the order belongs to (used to scope shopper-facing reads).</summary>
    public string BuyerId { get; private set; }

    public OrderNotificationType Type { get; private set; }

    /// <summary>Canonical E.164 recipient. PII — never log this value.</summary>
    public string ToNumber { get; private set; }

    /// <summary>Message text. PII — never log. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>Last known delivery status (see <see cref="MessageDeliveryStatus"/>).</summary>
    public string Status { get; private set; } = MessageDeliveryStatus.Pending;

    /// <summary>Provider (Twilio) delivery error code, populated only on failed/undelivered outcomes.</summary>
    public int? ErrorCode { get; private set; }

    /// <summary>The provider's own identifier for the message (Twilio SID), null if never handed off.</summary>
    public string? ProviderMessageSid { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>When the message was actually handed to the provider for immediate delivery.</summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>When a scheduled message is due to go out (the follow-up), null for immediate messages.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>True once the message body has been redacted at the provider and cleared locally.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>Caller-supplied idempotency key for a resend; unique so a repeat cannot send twice.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>For a resend, the id of the original notification it re-sends.</summary>
    public int? ResendOfNotificationId { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string buyerId, OrderNotificationType type, string toNumber, string body)
    {
        OrderId = orderId;
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Type = type;
        ToNumber = Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Body = Guard.Against.NullOrEmpty(body, nameof(body));
    }

    /// <summary>The provider accepted an immediate send; record its identifier and initial outcome.</summary>
    public void MarkSubmitted(string providerMessageSid, string status, int? errorCode, DateTimeOffset sentAt)
    {
        ProviderMessageSid = Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        Status = Guard.Against.NullOrEmpty(status, nameof(status));
        ErrorCode = errorCode;
        SentAt = sentAt;
    }

    /// <summary>The provider accepted a scheduled send; record its identifier and the due time.</summary>
    public void MarkScheduled(string providerMessageSid, string status, DateTimeOffset scheduledSendAt)
    {
        ProviderMessageSid = Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        Status = Guard.Against.NullOrEmpty(status, nameof(status));
        ScheduledSendAt = scheduledSendAt;
    }

    /// <summary>The provider rejected the create call — nothing was queued.</summary>
    public void MarkSubmissionFailed(int? errorCode)
    {
        Status = MessageDeliveryStatus.SubmissionFailed;
        ErrorCode = errorCode;
    }

    /// <summary>Refresh the stored outcome from the provider's authoritative state.</summary>
    public void UpdateDeliveryStatus(string status, int? errorCode)
    {
        Status = Guard.Against.NullOrEmpty(status, nameof(status));
        ErrorCode = errorCode;
    }

    /// <summary>A not-yet-sent scheduled message was called off before it went out.</summary>
    public void MarkCanceled()
    {
        Status = MessageDeliveryStatus.Canceled;
    }

    /// <summary>
    /// Dispose of the message content. The fact a message was sent and what became of it survives
    /// (identifier and status remain); only the body is cleared.
    /// </summary>
    public void RedactContent()
    {
        Body = null;
        ContentRedacted = true;
    }

    public void SetIdempotencyKey(string idempotencyKey)
    {
        IdempotencyKey = Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
    }

    public void SetResendOf(int originalNotificationId)
    {
        ResendOfNotificationId = originalNotificationId;
    }
}
