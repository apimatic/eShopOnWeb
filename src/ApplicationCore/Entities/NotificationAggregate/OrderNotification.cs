using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A record of a single text message that eShop asked the provider to send about an order.
/// It carries enough of the state the provider owns — the provider's message identifier
/// (<see cref="ProviderSid"/>) and the latest delivery outcome (<see cref="ProviderStatus"/>) — that a
/// later request can act on the message (cancel a scheduled follow-up, resend, dispose of its content)
/// and report on what became of it, not just the request that first sent it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(int orderId, string buyerId, NotificationType type, string toNumber, string body)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Type = type;
        ToNumber = toNumber;
        Body = body;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>The order this message is about.</summary>
    public int OrderId { get; private set; }

    /// <summary>The shopper the message is for. Used to keep one shopper's data from reaching another.</summary>
    public string BuyerId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>The destination number in canonical E.164. This is PII and is never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The text that was sent. Cleared once the content is disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's own identifier for the message (its message SID), once it has one.</summary>
    public string? ProviderSid { get; private set; }

    /// <summary>The latest delivery outcome as reported by the provider (queued, sent, delivered, undelivered, failed, scheduled, canceled, ...).</summary>
    public string? ProviderStatus { get; private set; }

    /// <summary>Provider error code, when the message failed or was undelivered.</summary>
    public int? ErrorCode { get; private set; }

    /// <summary>Provider error description, when the message failed or was undelivered.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>True for a message queued with the provider to be sent at a future time (the delivery follow-up).</summary>
    public bool IsScheduled { get; private set; }

    /// <summary>When a scheduled message is due to go out.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>True once the message content has been disposed of both locally and at the provider.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>The caller-supplied idempotency key for a resend, so a repeat under the same key sends nothing new.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When this record was created.</summary>
    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the provider last reported this message as sent (if known).</summary>
    public DateTimeOffset? ProviderSentAt { get; private set; }

    /// <summary>
    /// Records that this message was successfully handed to the provider. <paramref name="isScheduled"/> is true
    /// for the follow-up that is queued for a future <paramref name="scheduledSendAt"/>.
    /// </summary>
    public void RecordAccepted(string providerSid, string? status, bool isScheduled = false, DateTimeOffset? scheduledSendAt = null)
    {
        Guard.Against.NullOrEmpty(providerSid, nameof(providerSid));
        ProviderSid = providerSid;
        ProviderStatus = status;
        IsScheduled = isScheduled;
        ScheduledSendAt = scheduledSendAt;
        ErrorCode = null;
        ErrorMessage = null;
    }

    /// <summary>
    /// Records that the message could not be handed to the provider. This never fails the underlying
    /// order operation; it is captured here so an operator can see and resend it later.
    /// </summary>
    public void RecordSendFailure(string? reason)
    {
        ProviderStatus = "send_failed";
        ErrorMessage = reason;
    }

    /// <summary>Applies the latest delivery outcome pulled from the provider.</summary>
    public void ApplyProviderState(string? status, int? errorCode, string? errorMessage, DateTimeOffset? sentAt)
    {
        if (status is not null)
            ProviderStatus = status;
        ErrorCode = errorCode;
        ErrorMessage = errorMessage;
        if (sentAt is not null)
            ProviderSentAt = sentAt;
        if (!string.Equals(status, "scheduled", StringComparison.OrdinalIgnoreCase))
            IsScheduled = false;
    }

    /// <summary>Marks a scheduled message as cancelled after the provider has been told to call it off.</summary>
    public void MarkCanceled()
    {
        ProviderStatus = "canceled";
        IsScheduled = false;
    }

    /// <summary>Clears the local copy of the message text after the provider copy has been disposed of.</summary>
    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
    }

    /// <summary>Stamps the caller-supplied idempotency key that produced this (resend) message.</summary>
    public void SetIdempotencyKey(string key)
    {
        Guard.Against.NullOrEmpty(key, nameof(key));
        IdempotencyKey = key;
    }
}
