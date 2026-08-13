using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// The record of a single SMS message eShop sent (or tried to send) for an order as it moved
/// through its lifecycle. It carries enough of the state the provider owns — the provider's
/// message identifier (<see cref="MessageSid"/>) and current delivery outcome
/// (<see cref="DeliveryStatus"/>) — that a later request can act on it and report on it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
    // Local, pre-send lifecycle states. Once the provider accepts the message the provider's own
    // lowercase status values (queued, sent, delivered, undelivered, failed, scheduled, canceled, ...)
    // are stored verbatim.
    public const string StatusPending = "pending";
    public const string StatusFailed = "failed";
    public const string StatusScheduled = "scheduled";
    public const string StatusCanceled = "canceled";

    #pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }

    public OrderNotification(
        int orderId,
        string buyerId,
        NotificationType type,
        string toPhoneNumber,
        string body,
        bool isFollowUp = false,
        DateTimeOffset? scheduledSendAt = null)
    {
        Guard.Against.OutOfRange(orderId, nameof(orderId), 1, int.MaxValue);
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toPhoneNumber, nameof(toPhoneNumber));
        Guard.Against.NullOrEmpty(body, nameof(body));

        OrderId = orderId;
        BuyerId = buyerId;
        Type = type;
        ToPhoneNumber = toPhoneNumber;
        Body = body;
        IsFollowUp = isFollowUp;
        ScheduledSendAt = scheduledSendAt;
        DeliveryStatus = StatusPending;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }

    /// <summary>Identity of the shopper the message is about (used to scope reads).</summary>
    public string BuyerId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>The provider's message identifier, once the provider has accepted the message.</summary>
    public string? MessageSid { get; private set; }

    /// <summary>Current delivery outcome — a local sentinel before send, then the provider's own value.</summary>
    public string DeliveryStatus { get; private set; }

    /// <summary>The provider's numeric error code for an undelivered/failed message, if any.</summary>
    public int? ErrorCode { get; private set; }

    /// <summary>The destination number. Treated as PII — never logged.</summary>
    public string ToPhoneNumber { get; private set; }

    /// <summary>The message text. Cleared once its content has been disposed of.</summary>
    public string? Body { get; private set; }

    /// <summary>True once a shopper has asked for the content to be disposed of.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>True for the "how did the delivery go?" message scheduled at dispatch.</summary>
    public bool IsFollowUp { get; private set; }

    /// <summary>When a scheduled (future) message is due to be sent by the provider.</summary>
    public DateTimeOffset? ScheduledSendAt { get; private set; }

    /// <summary>The caller-supplied idempotency key that produced this record, if it was a resend.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>The notification this one was re-sent from, if any.</summary>
    public int? ResentFromNotificationId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastUpdatedAt { get; private set; }

    /// <summary>Records that the provider accepted the message and captured its identifier/state.</summary>
    public void MarkAccepted(string? messageSid, string? providerStatus, int? errorCode)
    {
        MessageSid = messageSid;
        DeliveryStatus = string.IsNullOrWhiteSpace(providerStatus) ? DeliveryStatus : providerStatus!;
        ErrorCode = errorCode;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records that the message could not be handed to the provider at all.</summary>
    public void MarkSendFailed()
    {
        DeliveryStatus = StatusFailed;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Records that a scheduled follow-up was cancelled before it went out.</summary>
    public void MarkCancelled(string? providerStatus)
    {
        DeliveryStatus = string.IsNullOrWhiteSpace(providerStatus) ? StatusCanceled : providerStatus!;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Refreshes the delivery outcome from the provider's current view of the message.</summary>
    public void RefreshDeliveryState(string? providerStatus, int? errorCode)
    {
        if (!string.IsNullOrWhiteSpace(providerStatus))
        {
            DeliveryStatus = providerStatus!;
        }

        ErrorCode = errorCode;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Disposes of the message content locally. The fact and outcome survive.</summary>
    public void DisposeContent()
    {
        Body = null;
        ContentDisposed = true;
        LastUpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Marks this record as the product of a resend of <paramref name="sourceNotificationId"/>.</summary>
    public void MarkAsResendOf(int sourceNotificationId, string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        ResentFromNotificationId = sourceNotificationId;
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>Whether the delivery outcome is settled and no longer worth re-querying.</summary>
    public bool IsTerminal() =>
        DeliveryStatus is "delivered" or "undelivered" or StatusFailed or StatusCanceled or "read";
}
