using System;
using Ardalis.GuardClauses;
using Microsoft.eShopWeb.ApplicationCore.Interfaces;

namespace Microsoft.eShopWeb.ApplicationCore.Entities.NotificationAggregate;

/// <summary>
/// A single SMS message that eShop asked the provider to send (or schedule) for an order.
/// It carries enough of the state the provider owns — the provider's message identifier
/// (<see cref="ProviderMessageSid"/>) and current delivery outcome (<see cref="DeliveryStatus"/>) —
/// that a later request can act on it (cancel/resend/redact) and report on it, not only the
/// request that first sent it.
/// </summary>
public class OrderNotification : BaseEntity, IAggregateRoot
{
#pragma warning disable CS8618 // Required by Entity Framework
    private OrderNotification() { }
#pragma warning restore CS8618

    private OrderNotification(int orderId, string buyerId, NotificationType type, string toNumber, string messageBody)
    {
        Guard.Against.NullOrEmpty(buyerId, nameof(buyerId));
        Guard.Against.NullOrEmpty(toNumber, nameof(toNumber));

        OrderId = orderId;
        BuyerId = buyerId;
        Type = type;
        ToNumber = toNumber;
        MessageBody = messageBody;
        DeliveryStatus = NotificationDeliveryState.Queued;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = CreatedAt;
    }

    /// <summary>Creates an immediate (send-now) notification.</summary>
    public static OrderNotification Immediate(int orderId, string buyerId, NotificationType type, string toNumber, string body)
        => new(orderId, buyerId, type, toNumber, body) { IsScheduled = false };

    /// <summary>Creates a notification that will be sent later by the provider at <paramref name="scheduledForUtc"/>.</summary>
    public static OrderNotification Scheduled(int orderId, string buyerId, NotificationType type, string toNumber, string body, DateTimeOffset scheduledForUtc)
        => new(orderId, buyerId, type, toNumber, body)
        {
            IsScheduled = true,
            ScheduledForUtc = scheduledForUtc,
            DeliveryStatus = NotificationDeliveryState.Scheduled
        };

    /// <summary>Creates a notification that re-sends the message of <paramref name="source"/> under an idempotency key.</summary>
    public static OrderNotification ResendOf(OrderNotification source, string idempotencyKey)
    {
        Guard.Against.Null(source, nameof(source));
        Guard.Against.NullOrEmpty(idempotencyKey, nameof(idempotencyKey));

        return new OrderNotification(source.OrderId, source.BuyerId, source.Type, source.ToNumber, source.MessageBody ?? string.Empty)
        {
            IsScheduled = false,
            IdempotencyKey = idempotencyKey,
            SourceNotificationId = source.Id
        };
    }

    public int OrderId { get; private set; }

    /// <summary>The owning shopper. Operator endpoints act across shoppers; shopper endpoints are scoped to this.</summary>
    public string BuyerId { get; private set; }

    public NotificationType Type { get; private set; }

    /// <summary>Destination number (E.164). Treated as sensitive — never logged.</summary>
    public string ToNumber { get; private set; }

    /// <summary>The message text. Null once the content has been disposed of (redacted).</summary>
    public string? MessageBody { get; private set; }

    /// <summary>The provider's identifier for this message, once the provider has accepted it. Null if the send never reached the provider.</summary>
    public string? ProviderMessageSid { get; private set; }

    /// <summary>Current delivery outcome. Provider wire value while the provider owns it; a local sentinel when the send never reached it.</summary>
    public string DeliveryStatus { get; private set; }

    public int? ProviderErrorCode { get; private set; }

    public string? ProviderErrorMessage { get; private set; }

    /// <summary>True when queued at the provider for later delivery (the delivery follow-up).</summary>
    public bool IsScheduled { get; private set; }

    public DateTimeOffset? ScheduledForUtc { get; private set; }

    /// <summary>Idempotency key supplied by the operator on a resend; null for messages not produced by a resend.</summary>
    public string? IdempotencyKey { get; private set; }

    /// <summary>When this message was produced by a resend, the notification it re-sent.</summary>
    public int? SourceNotificationId { get; private set; }

    /// <summary>True once the message content has been disposed of both locally and at the provider.</summary>
    public bool ContentRedacted { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Records that the provider accepted the message and returned an identifier and status.</summary>
    public void MarkSent(string providerMessageSid, string providerStatus)
    {
        Guard.Against.NullOrEmpty(providerMessageSid, nameof(providerMessageSid));
        ProviderMessageSid = providerMessageSid;
        DeliveryStatus = string.IsNullOrEmpty(providerStatus) ? NotificationDeliveryState.Queued : providerStatus;
        ProviderErrorCode = null;
        ProviderErrorMessage = null;
        Touch();
    }

    /// <summary>Records that the send never reached the provider (a transport or provider rejection). The order operation still succeeds.</summary>
    public void MarkSendFailed(int? errorCode, string? errorMessage)
    {
        DeliveryStatus = NotificationDeliveryState.SendFailed;
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>Refreshes the delivery outcome from the provider's current record.</summary>
    public void UpdateDeliveryStatus(string providerStatus, int? errorCode, string? errorMessage)
    {
        if (!string.IsNullOrEmpty(providerStatus))
        {
            DeliveryStatus = providerStatus;
        }
        ProviderErrorCode = errorCode;
        ProviderErrorMessage = errorMessage;
        Touch();
    }

    /// <summary>Records that a scheduled follow-up was called off with the provider before it went out.</summary>
    public void MarkCanceled()
    {
        DeliveryStatus = NotificationDeliveryState.Canceled;
        Touch();
    }

    /// <summary>Disposes of the message content locally. The record and its delivery outcome survive.</summary>
    public void RedactContent()
    {
        MessageBody = null;
        ContentRedacted = true;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTimeOffset.UtcNow;
}
