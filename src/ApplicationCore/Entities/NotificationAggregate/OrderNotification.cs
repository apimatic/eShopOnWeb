using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS message sent (or attempted, or scheduled) for an order as it moves through its
/// lifecycle. It carries enough of the state the provider owns — the provider's message identifier
/// (<see cref="ProviderMessageSid"/>) and its current delivery outcome (<see cref="Status"/>,
/// <see cref="ErrorCode"/>) — that a later request can act on the message (cancel a scheduled
/// follow-up, redact its content, re-send it) and report on it, not only the request that sent it.
/// The <see cref="Recipient"/> is PII and must never be written to logs.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string buyerId, NotificationType type, string recipient, string? body)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(recipient, nameof(recipient));

        OrderId = orderId;
        BuyerId = buyerId;
        Type = type;
        Recipient = recipient;
        Body = body;
        Status = NotificationStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The order this message is about.</summary>
    public int OrderId { get; private set; }

    /// <summary>The owning shopper (for scoping shopper-facing reads).</summary>
    public string BuyerId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>The destination number in E.164 form. PII — never logged.</summary>
    public string Recipient { get; private set; }

    /// <summary>The message text. Nulled out once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>True once a shopper's disposal request has redacted the content at the provider too.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>The provider's identifier for the message, once it accepted one.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>
    /// The last known delivery outcome. Either a provider status (queued, sent, delivered,
    /// undelivered, failed, scheduled, canceled, ...) or one of the local <see cref="NotificationStatus"/>
    /// sentinels for the cases the provider never saw (no number on file, a send that threw).
    /// </summary>
    public string Status { get; private set; }

    /// <summary>The provider error code on a failed/undelivered message, when present.</summary>
    public int? ErrorCode { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the provider accepted the message (used as the eShop-side reconciliation timestamp).</summary>
    public DateTimeOffset? SentAt { get; private set; }

    /// <summary>For a scheduled follow-up, when it is due to go out.</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    /// <summary>Caller-supplied idempotency key for the resend that produced this message, if any.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>Records the outcome of an accepted immediate send.</summary>
    public void RecordSend(string providerMessageSid, string status, int? errorCode)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = status;
        ErrorCode = errorCode;
        SentAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records that the message was accepted as a scheduled send due at <paramref name="scheduledFor"/>.</summary>
    public void RecordSchedule(string providerMessageSid, string status, DateTimeOffset scheduledFor)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = status;
        SentAt = DateTimeOffset.UtcNow;
        ScheduledFor = scheduledFor;
    }

    /// <summary>Records that the send attempt failed before the provider issued an identifier.</summary>
    public void RecordSendError(int? errorCode = null)
    {
        Status = NotificationStatus.SendError;
        ErrorCode = errorCode;
    }

    /// <summary>Advances the stored delivery outcome to the provider's current view.</summary>
    public void UpdateDeliveryState(string status, int? errorCode)
    {
        Guard.Against.NullOrEmpty(status, nameof(status));
        Status = status;
        ErrorCode = errorCode;
    }

    /// <summary>Marks a scheduled follow-up as called off before it went out.</summary>
    public void MarkCanceled()
    {
        Status = NotificationStatus.Canceled;
    }

    /// <summary>Clears the local content after it has been redacted at the provider.</summary>
    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
    }

    public void SetIdempotencyKey(string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        IdempotencyKey = idempotencyKey;
    }
}
