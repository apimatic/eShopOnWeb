using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS the shop created for an order: the message it tried to send, the provider's
/// identifier for it, and the current delivery outcome. It carries enough of the state the
/// provider owns (its <see cref="ProviderMessageSid"/> and <see cref="DeliveryStatus"/>) that a
/// later request can act on it — cancel, resend, redact, reconcile — not only the one that sent it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(
        int orderId,
        string ownerId,
        NotificationType type,
        string toNumber,
        string body,
        DateTimeOffset? scheduledFor = null,
        string? idempotencyKey = null)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(ownerId, nameof(ownerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        OwnerId = ownerId;
        Type = type;
        ToNumber = toNumber;
        Body = body;
        ScheduledFor = scheduledFor;
        IdempotencyKey = idempotencyKey;
        DeliveryStatus = DeliveryStatuses.NotSent;
    }

    /// <summary>The order this message is about.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper who owns the order; used to scope shopper-facing queries.</summary>
    public string OwnerId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>The destination E.164 number. Sensitive: never logged.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Cleared to null once its content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for the message (Twilio message SID), once it has one.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery status (or <see cref="DeliveryStatuses.NotSent"/>).</summary>
    public string DeliveryStatus { get; private set; }

    /// <summary>The provider's error code for a failed/undelivered message, if any.</summary>
    public int? ErrorCode { get; private set; }

    /// <summary>A short, number-free note when the app could not hand the message to the provider.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>When a scheduled message is due to be sent by the provider.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>The caller-supplied idempotency key that produced this message, when it was a resend.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>Whether this message's content has been disposed of (body cleared here and redacted at the provider).</summary>
    public bool ContentDisposed { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Records the provider's acknowledgement of a send (or schedule): its SID and status.</summary>
    public void RecordSendResult(string providerMessageSid, string status, int? errorCode)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        Guard.Against.NullOrEmpty(status, nameof(status));

        ProviderMessageSid = providerMessageSid;
        DeliveryStatus = status;
        ErrorCode = errorCode;
        FailureReason = null;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records that the app never reached the provider. The underlying order operation still succeeds.</summary>
    public void RecordSendFailure(string reason)
    {
        DeliveryStatus = DeliveryStatuses.NotSent;
        FailureReason = reason;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Refreshes the delivery outcome from a later read of the provider's record.</summary>
    public void UpdateDeliveryStatus(string status, int? errorCode)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        DeliveryStatus = status;
        ErrorCode = errorCode;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Marks a previously scheduled message as cancelled at the provider before it went out.</summary>
    public void MarkCanceled()
    {
        DeliveryStatus = DeliveryStatuses.Canceled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Clears the local message text once its content has been disposed of at the provider too.</summary>
    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
