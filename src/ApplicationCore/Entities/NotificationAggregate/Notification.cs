using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS message sent (or scheduled) about an order as it moves. It carries enough of the
/// state the provider owns — the provider's message identifier (<see cref="ProviderMessageSid"/>)
/// and the current delivery outcome (<see cref="Status"/>) — that a later request can act on it
/// (fetch status, cancel, redact, resend) and report on it, not only the request that sent it.
/// A notification belongs to exactly one shopper (<see cref="BuyerId"/>).
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
    public string BuyerId { get; private set; }
    public int OrderId { get; private set; }
    public NotificationType Type { get; private set; }

    /// <summary>The provider-canonical E.164 destination. Never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Disposed (set to null) when a shopper asks for the content to be removed.</summary>
    public string? MessageBody { get; private set; }

    /// <summary>The provider's message identifier (SID), once the provider has accepted the message.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Current delivery outcome — mirrors the provider's status, or a local state (see <see cref="NotificationStatuses"/>).</summary>
    public string Status { get; private set; }

    /// <summary>True once the message body has been disposed at the provider and locally.</summary>
    public bool ContentRedacted { get; private set; }

    /// <summary>The caller-supplied idempotency key that produced this notification via a resend, if any.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>When a future (follow-up) message is queued with the provider to be sent.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

#pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }
#pragma warning restore CS8618

    public Notification(string buyerId, int orderId, NotificationType type, string toNumber, string messageBody, string? idempotencyKey = null)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.Null(messageBody, nameof(messageBody));

        BuyerId = buyerId;
        OrderId = orderId;
        Type = type;
        ToNumber = toNumber;
        MessageBody = messageBody;
        IdempotencyKey = idempotencyKey;
        Status = NotificationStatuses.Pending;
    }

    /// <summary>Record that the provider accepted the message for immediate delivery.</summary>
    public void MarkSent(string providerMessageSid, string? providerStatus)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = string.IsNullOrWhiteSpace(providerStatus) ? NotificationStatuses.Pending : providerStatus!;
    }

    /// <summary>Record that the provider accepted the message for future (scheduled) delivery.</summary>
    public void MarkScheduled(string providerMessageSid, string? providerStatus, DateTimeOffset scheduledSendAt)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = string.IsNullOrWhiteSpace(providerStatus) ? NotificationStatuses.Scheduled : providerStatus!;
        ScheduledSendAt = scheduledSendAt;
    }

    /// <summary>Record that the message never reached the shopper (rejected by, or unreachable at, the provider).</summary>
    public void MarkSendFailed() => Status = NotificationStatuses.SendFailed;

    /// <summary>Record that a scheduled message was called off before it went out.</summary>
    public void MarkCanceled() => Status = NotificationStatuses.Canceled;

    /// <summary>Refresh the delivery outcome from the provider's current status.</summary>
    public void UpdateDeliveryStatus(string? providerStatus)
    {
        if (!string.IsNullOrWhiteSpace(providerStatus))
            Status = providerStatus!;
    }

    /// <summary>Dispose the message content locally (after it has also been disposed at the provider).</summary>
    public void RedactContent()
    {
        MessageBody = null;
        ContentRedacted = true;
    }
}
