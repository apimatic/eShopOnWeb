using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS the shop tried to send about an order. It carries enough of the state the
/// provider owns — the provider's message identifier (<see cref="MessageSid"/>) and its
/// current delivery outcome (<see cref="ProviderStatus"/>) — that a later request can act on
/// it (fetch, cancel, redact, resend) and report on it, not only the request that created it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    public int OrderId { get; private set; }

    /// <summary>Owner of the order this message is about; lets us scope without loading the order.</summary>
    public string BuyerId { get; private set; }

    public NotificationKind Kind { get; private set; }

    /// <summary>Canonical destination the message was addressed to. Never logged.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The text we asked the provider to send. Cleared when the shopper disposes of the content.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's identifier for this message (its SID). Null until the provider accepts it.</summary>
    public string? MessageSid { get; private set; }

    /// <summary>The provider's current delivery outcome (raw wire value, e.g. "queued", "delivered", "undelivered").</summary>
    public string? ProviderStatus { get; private set; }

    /// <summary>Provider error code, when the provider reports one. Never contains a phone number or secret.</summary>
    public string? ErrorCode { get; private set; }

    /// <summary>True while this message is queued with the provider for a future send and has not gone out yet.</summary>
    public bool IsScheduled { get; private set; }

    /// <summary>When a scheduled message is due to go out.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>True once the message text has been disposed of at the provider and here.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>Idempotency key supplied by the operator when this message was produced by a resend.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When set, the notification this one is a resend of.</summary>
    public int? ResendOfNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastStatusCheckedAt { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string buyerId, NotificationKind kind, string toNumber, string body)
    {
        OrderId = Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        BuyerId = Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Kind = kind;
        ToNumber = Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Body = Guard.Against.NullOrEmpty(body, nameof(body));
    }

    /// <summary>The provider accepted the message for immediate delivery.</summary>
    public void MarkSent(string messageSid, string? providerStatus)
    {
        MessageSid = Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        ProviderStatus = providerStatus;
        IsScheduled = false;
        LastStatusCheckedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The provider accepted the message for a future send.</summary>
    public void MarkScheduled(string messageSid, string? providerStatus, DateTimeOffset sendAt)
    {
        MessageSid = Guard.Against.NullOrEmpty(messageSid, nameof(messageSid));
        ProviderStatus = providerStatus;
        IsScheduled = true;
        ScheduledSendAt = sendAt;
        LastStatusCheckedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The message never reached the provider. Records a local, phone-free outcome; the order operation still succeeds.</summary>
    public void MarkSendFailed(string? errorCode)
    {
        ProviderStatus = "not_sent";
        ErrorCode = errorCode;
        IsScheduled = false;
        LastStatusCheckedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Refresh the delivery outcome from the provider.</summary>
    public void UpdateStatus(string? providerStatus, string? errorCode)
    {
        if (providerStatus is not null)
        {
            ProviderStatus = providerStatus;
            IsScheduled = string.Equals(providerStatus, "scheduled", StringComparison.OrdinalIgnoreCase);
        }
        if (errorCode is not null)
        {
            ErrorCode = errorCode;
        }
        LastStatusCheckedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The scheduled send was called off before it went out.</summary>
    public void MarkScheduledCanceled()
    {
        ProviderStatus = "canceled";
        IsScheduled = false;
        LastStatusCheckedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The message text has been disposed of at the provider; drop the local copy too.</summary>
    public void MarkContentRedacted()
    {
        ContentRedacted = true;
        Body = null;
    }

    public void SetIdempotency(string idempotencyKey, int resendOfNotificationId)
    {
        IdempotencyKey = Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        ResendOfNotificationId = resendOfNotificationId;
    }
}
