using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single message about an order. It carries enough of the state the provider owns — the
/// provider's message identifier and the current delivery outcome — that a later request can
/// act on it (resend, dispose of its content, cancel it) and report on it, not only the request
/// that first sent it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, NotificationType type, string toPhoneNumber, string body)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Type = type;
        ToPhoneNumber = toPhoneNumber;
        Body = body;
    }

    public int OrderId { get; private set; }

    /// <summary>The owning shopper (the order's buyer id).</summary>
    public string BuyerId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>Canonical E.164 destination. Sensitive: never written to logs.</summary>
    public string ToPhoneNumber { get; private set; }

    /// <summary>The message text. Nulled once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for the message (Twilio message SID). Null if the provider never accepted it.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome, stored verbatim. See <see cref="MessageDeliveryStatus"/>.</summary>
    public string Status { get; private set; } = MessageDeliveryStatus.SendError;

    /// <summary>The provider's error code for a failed/undelivered message, if any.</summary>
    public string? ErrorCode { get; private set; }

    /// <summary>True once the message text has been disposed of at the provider and locally.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>The caller-supplied idempotency key that produced this message, when it was created by a re-send.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>For a message queued for a future send, when the provider will attempt it.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>When <see cref="Status"/> was last refreshed from the provider.</summary>
    public DateTimeOffset? StatusUpdatedAt { get; private set; }

    public void SetIdempotencyKey(string? key) => IdempotencyKey = key;

    public void SetScheduledFor(DateTimeOffset when) => ScheduledFor = when;

    /// <summary>Record that the provider accepted the message and gave it an identifier and an initial status.</summary>
    public void RecordProviderMessage(string sid, string status, string? errorCode = null)
    {
        Guard.Against.NullOrEmpty(sid, nameof(sid));
        ProviderMessageSid = sid;
        Status = status;
        ErrorCode = errorCode;
        StatusUpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Record that the provider never accepted the request, so there is no message to track.</summary>
    public void RecordSendError(string? errorCode = null)
    {
        Status = MessageDeliveryStatus.SendError;
        ErrorCode = errorCode;
        StatusUpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Refresh the delivery outcome from a later provider read.</summary>
    public void UpdateDeliveryStatus(string status, string? errorCode)
    {
        Status = status;
        if (!string.IsNullOrEmpty(errorCode))
        {
            ErrorCode = errorCode;
        }
        StatusUpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Record that the content has been disposed of, keeping the fact of the message and its outcome.</summary>
    public void MarkContentRedacted()
    {
        ContentRedacted = true;
        Body = null;
    }
}
