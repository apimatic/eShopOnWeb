using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS message the shop sent (or attempted to send) to a shopper about one of
/// their orders. It carries enough of the state the provider owns — the provider's message
/// identifier (<see cref="ProviderSid"/>) and current delivery outcome (<see cref="ProviderStatus"/>) —
/// that a later request can act on the message (resend, dispose, cancel a scheduled follow-up)
/// and report on it, not only the request that first sent it.
/// </summary>
public class Notification : BaseEntity, IAggregateRoot
{
    /// <summary>Local marker used when the provider never accepted the message (e.g. a transient
    /// error while calling the provider). Distinct from every provider status value.</summary>
    public const string NotSentStatus = "not_sent";

    #pragma warning disable CS8618 // Required by Entity Framework
    private Notification() { }

    public Notification(int orderId, string ownerId, NotificationType type, string toNumber, string body,
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
        IdempotencyKey = idempotencyKey;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>The order this message is about.</summary>
    public int OrderId { get; private set; }

    /// <summary>Identity (user name) of the shopper the message is about. Used for scoping.</summary>
    public string OwnerId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>Provider-canonical E.164 destination. Never written to logs.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>The provider's own identifier for this message, once it accepted it.</summary>
    public string? ProviderSid { get; private set; }

    /// <summary>The provider's current delivery outcome (e.g. queued, sent, delivered, undelivered,
    /// failed, scheduled, canceled), or <see cref="NotSentStatus"/> when it was never accepted.</summary>
    public string? ProviderStatus { get; private set; }

    public int? ProviderErrorCode { get; private set; }

    public string? ProviderErrorMessage { get; private set; }

    /// <summary>When set, the provider was asked to send this message at that future time
    /// (a scheduled follow-up). Null for immediate messages.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>The provider's timestamp for when the message was sent, when known.</summary>
    public DateTimeOffset? ProviderSentAt { get; private set; }

    /// <summary>Caller-supplied idempotency key for a resend, when this message was produced by one.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>True once the message content has been disposed of at the shopper's request.</summary>
    public bool ContentDisposed { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Records the outcome of accepting the message with the provider.</summary>
    public void RecordProviderAccepted(string? sid, string status, int? errorCode, string? errorMessage,
        DateTimeOffset? scheduledSendAt = null, DateTimeOffset? sentAt = null)
    {
        ProviderSid = sid;
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        if (scheduledSendAt.HasValue) ScheduledSendAt = scheduledSendAt;
        if (sentAt.HasValue) ProviderSentAt = sentAt;
        Touch();
    }

    /// <summary>Records that the provider never accepted the message (transient failure).
    /// The underlying order operation still succeeds; the message is simply not sent.</summary>
    public void RecordNotSent(string? reason)
    {
        ProviderStatus = NotSentStatus;
        ProviderErrorMessage = reason;
        Touch();
    }

    /// <summary>Refreshes the delivery outcome from the provider's latest record of the message.</summary>
    public void UpdateDeliveryState(string status, int? errorCode, string? errorMessage, DateTimeOffset? sentAt = null)
    {
        ProviderStatus = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        if (sentAt.HasValue) ProviderSentAt = sentAt;
        Touch();
    }

    /// <summary>Disposes of the message content locally. The provider-side redaction is performed
    /// separately; this records that the local copy is gone while the fact of the send survives.</summary>
    public void MarkContentDisposed()
    {
        Body = null;
        ContentDisposed = true;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
