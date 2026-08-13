using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The record of a single SMS the shop tried to send a shopper about one of their orders.
/// It carries enough of the state the provider owns — the provider's identifier
/// (<see cref="ProviderMessageSid"/>) and the current delivery outcome (<see cref="DeliveryStatus"/>) —
/// that a later request can act on it (resend, cancel a scheduled follow-up, dispose of its content)
/// and report on it, not only the request that first sent it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    /// <summary>Synthetic status recorded when the provider could not be reached / rejected the send outright.</summary>
    public const string SendFailedStatus = "send_failed";

    public int OrderId { get; private set; }

    /// <summary>Owner of the order this notification is about (the authenticated username).</summary>
    public string BuyerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>The canonical destination number. Never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Cleared once the content has been disposed of at the shopper's request.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's message identifier (SID). Null when the send never reached the provider.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome (its wire value), or <see cref="SendFailedStatus"/>.</summary>
    public string? DeliveryStatus { get; private set; }

    /// <summary>True for a message queued with the provider to go out in the future (the delivery follow-up).</summary>
    public bool IsScheduled { get; private set; }

    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>True once the message content has been disposed of; the send-record and outcome survive.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>Caller-supplied idempotency key for a resend, so a repeat under the same key does not re-send.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When this notification was produced by re-sending another, the id of that original.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string buyerId, NotificationKind kind, string toNumber, string body)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Kind = kind;
        ToNumber = toNumber;
        Body = body;
    }

    /// <summary>Records the identifier and status the provider returned for a successful create/schedule.</summary>
    public void RecordSendResult(string sid, string? status)
    {
        Guard.Against.NullOrEmpty(sid, nameof(sid));
        ProviderMessageSid = sid;
        DeliveryStatus = status;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records that the message could not be handed to the provider at all.</summary>
    public void RecordSendFailure()
    {
        DeliveryStatus = SendFailedStatus;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Marks this as a future-dated message queued with the provider.</summary>
    public void MarkScheduled(DateTimeOffset sendAt)
    {
        IsScheduled = true;
        ScheduledSendAt = sendAt;
    }

    /// <summary>Refreshes the delivery outcome from the provider's current record.</summary>
    public void UpdateDeliveryStatus(string? status)
    {
        if (status is null || status == DeliveryStatus)
        {
            return;
        }

        DeliveryStatus = status;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records that a not-yet-sent scheduled message was called off before it went out.</summary>
    public void MarkCanceled()
    {
        DeliveryStatus = "canceled";
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Disposes of the message content. The fact a message was sent, and its outcome, survive.</summary>
    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Links a notification produced by a resend back to the original it re-sent.</summary>
    public void MarkAsResendOf(int originalNotificationId, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        ResendOfNotificationId = originalNotificationId;
        IdempotencyKey = idempotencyKey;
    }
}
