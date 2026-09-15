using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.OrderNotificationAggregate;

/// <summary>
/// A single SMS message sent (or scheduled, or attempted) about an order. This is the record
/// the operator endpoints act on by <see cref="BaseEntity.Id"/> (the notificationId).
///
/// It carries enough of the state the provider owns - its message identifier
/// (<see cref="ProviderMessageSid"/>) and current delivery outcome (<see cref="Status"/>,
/// plus any error code/message) - that a later request can act on it and report on it,
/// not just the one that sent it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    public OrderNotification(int orderId, string buyerId, OrderNotificationType type, string toNumber, string content)
    {
        Guard.Against.NegativeOrZero(orderId, nameof(orderId));
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));
        Guard.Against.Null(content, nameof(content));

        OrderId = orderId;
        BuyerId = buyerId;
        Type = type;
        ToNumber = toNumber;
        Content = content;
        Status = NotificationStatuses.NotSent;
        CreatedDate = DateTimeOffset.UtcNow;
    }

    public int OrderId { get; private set; }
    public string BuyerId { get; private set; }
    public OrderNotificationType Type { get; private set; }

    /// <summary>The destination number in E.164. Persisted so a resend can reach it, but never logged.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of.</summary>
    public string? Content { get; private set; }

    /// <summary>True once the shopper's disposal request has redacted the content here and at the provider.</summary>
    public bool ContentDisposed { get; private set; }

    /// <summary>The provider's own identifier for the message (its SID). Null if the send never got that far.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>The current delivery outcome, mirroring the provider's status vocabulary. See <see cref="NotificationStatuses"/>.</summary>
    public string Status { get; private set; }

    public int? ProviderErrorCode { get; private set; }
    public string? ProviderErrorMessage { get; private set; }

    /// <summary>The caller-supplied idempotency key, when this message was produced by an operator resend.</summary>
    public string? IdempotencyKey { get; private set; }

    public DateTimeOffset CreatedDate { get; private set; }

    /// <summary>When a scheduled message is due to be sent by the provider (follow-ups only).</summary>
    public DateTimeOffset? ScheduledFor { get; private set; }

    public void SetIdempotencyKey(string idempotencyKey)
    {
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));
        IdempotencyKey = idempotencyKey;
    }

    /// <summary>Records that the provider accepted the message (immediate send or scheduled).</summary>
    public void RecordProviderAccepted(string providerMessageSid, string status, int? errorCode, string? errorMessage, DateTimeOffset? scheduledFor)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        Status = string.IsNullOrEmpty(status) ? NotificationStatuses.Accepted : status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        ScheduledFor = scheduledFor;
    }

    /// <summary>Records that the message was never handed to the provider (e.g. the create call failed).</summary>
    public void RecordNotSent(string? reason)
    {
        Status = NotificationStatuses.NotSent;
        ProviderErrorMessage = reason;
    }

    /// <summary>Refreshes the delivery outcome from the provider.</summary>
    public void UpdateDeliveryState(string status, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(status))
            Status = status;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
    }

    public void MarkScheduledCancelled()
    {
        Status = NotificationStatuses.Canceled;
    }

    /// <summary>Disposes of the message text locally. The provider-side redaction is done by the service.</summary>
    public void MarkContentDisposed()
    {
        Content = null;
        ContentDisposed = true;
    }
}
